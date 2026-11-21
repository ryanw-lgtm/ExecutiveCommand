using System.Text.Json.Nodes;
using ExecutiveCommand.Core.Engine;
using ExecutiveCommand.Core.Model;

namespace ExecutiveCommand.Core.Actions;

public sealed record HandlerResult(string Summary, List<string> Effects);

public abstract class ActionHandler
{
    public abstract ActionSpec Spec { get; }
    /// <summary>Domain rules: authority, money availability, state preconditions.</summary>
    public abstract ValidationResult ValidateDomain(GameState s, ActionProposal a);
    /// <summary>Non-null when the action is high-impact and must be explicitly confirmed by the player.</summary>
    public virtual string? ConfirmationPrompt(GameState s, ActionProposal a) => null;
    public abstract string TopicOf(GameState s, ActionProposal a);
    public abstract HandlerResult Execute(SimulationEngine e, ActionProposal a, DecisionRecord decision);
}

/// <summary>Allowlist of every action that may change game state. Anything not registered is rejected.</summary>
public sealed class ActionRegistry
{
    private readonly Dictionary<string, ActionHandler> _handlers = new(StringComparer.Ordinal);

    public void Register(ActionHandler h) => _handlers[h.Spec.Name] = h;
    public ActionHandler? Get(string name) => _handlers.TryGetValue(name, out var h) ? h : null;
    public IEnumerable<ActionSpec> Specs => _handlers.Values.Select(h => h.Spec);

    public static ActionRegistry CreateDefault()
    {
        var r = new ActionRegistry();
        r.Register(new DelegateAssignmentHandler());
        r.Register(new RequestAssessmentHandler());
        r.Register(new ApproveProposalHandler());
        r.Register(new RejectProposalHandler());
        r.Register(new DeferProposalHandler());
        r.Register(new ReduceBudgetHandler());
        r.Register(new IncidentResponseHandler());
        return r;
    }

    /// <summary>
    /// JSON Schema for the interpreter output, generated from the registry so the LLM grammar and the
    /// validator can never drift apart.
    /// </summary>
    public JsonObject InterpreterSchema()
    {
        var paramProps = new JsonObject();
        foreach (var p in _handlers.Values.SelectMany(h => h.Spec.Params).GroupBy(p => p.Name).Select(g => g.First()))
        {
            JsonObject prop = p.Type switch
            {
                ParamType.Integer => new JsonObject { ["type"] = "integer" },
                ParamType.Money => new JsonObject { ["type"] = "number" },
                ParamType.Boolean => new JsonObject { ["type"] = "boolean" },
                _ => new JsonObject { ["type"] = "string" },
            };
            paramProps[p.Name] = prop;
        }
        var actionNames = new JsonArray(_handlers.Keys.OrderBy(k => k).Select(k => (JsonNode)JsonValue.Create(k)!).ToArray());
        var roles = new JsonArray(Roles.Addressable.Select(r => (JsonNode)JsonValue.Create(r.ToString())!).Append(JsonValue.Create("none")!).ToArray());
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["intent"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("action", "question", "claim", "statement") },
                ["addressed_role"] = new JsonObject { ["type"] = "string", ["enum"] = roles },
                ["action"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["name"] = new JsonObject { ["type"] = "string", ["enum"] = actionNames },
                        ["params"] = new JsonObject { ["type"] = "object", ["properties"] = paramProps },
                    },
                    ["required"] = new JsonArray("name", "params"),
                },
                ["clarification"] = new JsonObject { ["type"] = "string" },
            },
            ["required"] = new JsonArray("intent", "addressed_role"),
        };
    }
}

public static class ActionValidator
{
    public static ValidationResult Validate(GameState s, ActionRegistry registry, ActionProposal a)
    {
        var h = registry.Get(a.Name);
        if (h == null) return ValidationResult.Fail($"'{a.Name}' is not an action available to you.");

        foreach (var key in a.Params.Select(kv => kv.Key))
            if (!h.Spec.Params.Any(p => p.Name == key))
                return ValidationResult.Fail($"Unexpected parameter '{key}' for {a.Name}.");

        foreach (var p in h.Spec.Params)
        {
            bool present = a.Params.ContainsKey(p.Name) && a.Params[p.Name] != null;
            if (!present)
            {
                if (p.Required) return ValidationResult.Clarify(MissingQuestion(a.Name, p));
                continue;
            }
            var err = CheckType(a, p);
            if (err != null) return ValidationResult.Fail(err);
        }

        var domain = h.ValidateDomain(s, a);
        if (!domain.IsValid) return domain;
        var prompt = h.ConfirmationPrompt(s, a);
        return prompt != null ? ValidationResult.Confirm(prompt) : ValidationResult.Ok();
    }

    private static string MissingQuestion(string action, ParamSpec p) => p.Name switch
    {
        "deadline_days" => "By when do you want this back? Give me a timeframe.",
        "assignee_role" or "from_role" => "Who should own this?",
        "proposal_id" => "Which proposal are you referring to?",
        "annual_amount" => "How much do you want to take out of the budget?",
        "budget_line" => "Which budget line — consulting, training, travel, software, hardware or cloud?",
        "task_type" => "What kind of work do you want done — an assessment, a cost review, a proof of concept, a negotiation?",
        _ => $"I need the {p.Name.Replace('_', ' ')} before I can act on that.",
    };

    private static string? CheckType(ActionProposal a, ParamSpec p)
    {
        switch (p.Type)
        {
            case ParamType.Integer:
                var i = a.Int(p.Name);
                if (i == null) return $"{p.Name} must be a whole number.";
                if (p.Min != null && i < p.Min) return $"{p.Name} must be at least {p.Min}.";
                if (p.Max != null && i > p.Max) return $"{p.Name} must be at most {p.Max}.";
                break;
            case ParamType.Money:
                var m = a.Money(p.Name);
                if (m == null) return $"{p.Name} must be a dollar amount.";
                if (p.Min != null && m < (decimal)p.Min) return $"{p.Name} must be at least {SimulationEngine.Money((decimal)p.Min)}.";
                if (p.Max != null && m > (decimal)p.Max) return $"{p.Name} cannot exceed {SimulationEngine.Money((decimal)p.Max)}.";
                break;
            case ParamType.Boolean:
                if (a.Bool(p.Name) == null) return $"{p.Name} must be true or false.";
                break;
            case ParamType.Enum:
                var e = a.Str(p.Name);
                if (e == null || p.EnumValues == null || !p.EnumValues.Contains(e, StringComparer.OrdinalIgnoreCase))
                    return $"{p.Name} must be one of: {string.Join(", ", p.EnumValues ?? Array.Empty<string>())}.";
                break;
            case ParamType.String:
                var str = a.Str(p.Name);
                if (str == null) return $"{p.Name} must be text.";
                if (str.Length > p.MaxLength) return $"{p.Name} is too long.";
                break;
        }
        return null;
    }
}

public static class Roles
{
    public static readonly ExecRole[] Addressable =
        { ExecRole.CEO, ExecRole.CFO, ExecRole.CIO, ExecRole.CISO, ExecRole.CMO, ExecRole.CNO, ExecRole.EADirector, ExecRole.Vendor };

    public static ExecRole Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return ExecRole.None;
        var t = s.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
        return t switch
        {
            "ceo" or "chiefexecutive" or "chiefexecutiveofficer" => ExecRole.CEO,
            "cfo" or "chieffinancialofficer" or "finance" => ExecRole.CFO,
            "cio" or "chiefinformationofficer" => ExecRole.CIO,
            "ciso" or "chiefinformationsecurityofficer" or "security" => ExecRole.CISO,
            "cmo" or "chiefmedicalofficer" => ExecRole.CMO,
            "cno" or "chiefnursingofficer" => ExecRole.CNO,
            "eadirector" or "ea" or "enterprisearchitect" or "enterprisearchitecture" or "enterprisearchitecturedirector" or "architect" => ExecRole.EADirector,
            "vendor" or "halcyon" => ExecRole.Vendor,
            "cto" => ExecRole.CTO,
            _ => Enum.TryParse<ExecRole>(s, true, out var r) ? r : ExecRole.None,
        };
    }

    public static string Label(ExecRole r) => r switch
    {
        ExecRole.EADirector => "Enterprise Architecture Director",
        ExecRole.Vendor => "EHR Vendor",
        _ => r.ToString(),
    };
}
