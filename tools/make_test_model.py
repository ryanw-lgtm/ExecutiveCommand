#!/usr/bin/env python3
"""
Builds a tiny RANDOM-WEIGHT llama-architecture GGUF for integration-testing the inference plumbing
(process management, health checks, grammar-constrained JSON, timeouts, fallback).

It is NOT a usable language model: its text is gibberish. It exists because CI and offline dev
environments may not be able to download real weights. Usage:

    python3 tools/make_test_model.py <llama.cpp>/models/ggml-vocab-qwen2.gguf models/test-tiny-random.gguf

Requires numpy and llama.cpp's gguf-py (add <llama.cpp>/gguf-py to PYTHONPATH).
"""
import sys
import numpy as np
import gguf

vocab_path, out_path = sys.argv[1], sys.argv[2]
reader = gguf.GGUFReader(vocab_path)


tokenizer_fields = {k: f for k, f in reader.fields.items() if k.startswith("tokenizer.")}
n_tokens = len(tokenizer_fields["tokenizer.ggml.tokens"].data)
n_vocab, n_embd, n_head, n_layer, n_ff, n_ctx = n_tokens, 64, 4, 2, 128, 4096

w = gguf.GGUFWriter(out_path, "llama")
w.add_name("executive-command-test-tiny-random")
w.add_context_length(n_ctx)
w.add_embedding_length(n_embd)
w.add_block_count(n_layer)
w.add_feed_forward_length(n_ff)
w.add_head_count(n_head)
w.add_head_count_kv(n_head)
w.add_rope_dimension_count(n_embd // n_head)
w.add_layer_norm_rms_eps(1e-5)
w.add_file_type(gguf.LlamaFileType.ALL_F32)
# Copy every tokenizer field verbatim (model, pre-tokenizer, tokens, merges, special ids, template).
for key, f in tokenizer_fields.items():
    vtype = f.types[0]
    sub = f.types[1] if vtype == gguf.GGUFValueType.ARRAY else None
    w.add_key_value(key, f.contents(), vtype, sub)
if "tokenizer.chat_template" not in tokenizer_fields:
    # Standard ChatML fallback for vocab files without a template.
    w.add_chat_template(
        "{% for m in messages %}{{ '<|im_start|>' + m['role'] + '\\n' + m['content'] + '<|im_end|>' + '\\n' }}{% endfor %}"
        "{% if add_generation_prompt %}{{ '<|im_start|>assistant\\n' }}{% endif %}"
    )

rng = np.random.default_rng(1234)


def r(*shape):
    return (rng.standard_normal(shape) * 0.02).astype(np.float32)


w.add_tensor("token_embd.weight", r(n_vocab, n_embd))
w.add_tensor("output_norm.weight", np.ones(n_embd, dtype=np.float32))
w.add_tensor("output.weight", r(n_vocab, n_embd))
for i in range(n_layer):
    p = f"blk.{i}."
    w.add_tensor(p + "attn_norm.weight", np.ones(n_embd, dtype=np.float32))
    for t in ("attn_q", "attn_k", "attn_v", "attn_output"):
        w.add_tensor(p + t + ".weight", r(n_embd, n_embd))
    w.add_tensor(p + "ffn_norm.weight", np.ones(n_embd, dtype=np.float32))
    w.add_tensor(p + "ffn_gate.weight", r(n_ff, n_embd))
    w.add_tensor(p + "ffn_up.weight", r(n_ff, n_embd))
    w.add_tensor(p + "ffn_down.weight", r(n_embd, n_ff))

w.write_header_to_file()
w.write_kv_data_to_file()
w.write_tensors_to_file()
w.close()
print(f"wrote {out_path} (vocab {n_vocab}, random weights — for plumbing tests only)")
