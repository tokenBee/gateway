# TokenBee TypeScript SDK

Official TypeScript/JavaScript SDK for [TokenBee](https://tokenbee.io) - The Intelligent LLM Inference Gateway with Observability, Compression, and Privacy.

## Features

- **Unified API**: Access multiple LLM providers (OpenAI, Anthropic, Google, Mistral, etc.) through a single interface.
- **Intelligent Compression**: Reduce token usage and latency with context-aware compression.
- **Privacy Guard**: Automatic PII masking and privacy-preserving inference.
- **Built-in Observability**: Automatic tracking of latency, costs, and token usage.

## Links

- **Homepage**: [https://tokenbee.io](https://tokenbee.io)
- **Dashboard**: [https://tokenbee.io/dashboard](https://tokenbee.io/dashboard)

## Installation

```bash
npm install @tokenbee/sdk
```

## Quick Start

```typescript
import { TokenBee, TokenBeeModel, CompressionRate } from '@tokenbee/sdk';

// Initialize the client with your TokenBee API key 
// AND your LLM provider key (Bring Your Own Key - BYOK)
const client = new TokenBee({
  apiKey: 'your_tokenbee_api_key',
  llmKey: 'your_llm_provider_key', // e.g. OpenAI or Anthropic key
});

// Send a request
const res = await client.send({
  model: TokenBeeModel.AnthropicClaude3_5_Sonnet,
  input: {
    messages: [{ role: 'user', content: 'Explain quantum entanglement' }],
    compression: 'auto',
    rate: CompressionRate.High
  }
});

console.log(res);
```

## Compression Control

You can specify the compression rate and method per request. TokenBee uses an intelligent semantic engine to reduce token usage while preserving meaning.

```typescript
const res = await client.send({
  model: TokenBeeModel.AnthropicClaude3_5_Sonnet,
  input: {
    messages: [...],
    compression: 'auto',      // 'auto' (default), 'on', or 'off'
    rate: CompressionRate.High, // Medium (0.5), High (0.33), etc.
    privacy: true
  }
});
```

- **`compression`**: Set to `'auto'` to let TokenBee decide when to compress, or `'off'` to bypass the compression engine entirely for high-precision tasks.
- **`rate`**: Controls the aggressiveness of compression. `High` aims for ~67% token reduction.
- **`sessionId`**: (Optional) String ID to group multiple requests into a single replayable session in the dashboard.
- **`userId`**: (Optional) String ID to track usage and costs per unique end-user.
- **`privacy`**: (Optional) Set to `true` to disable payload logging and session replays for this request. Metadata (latency, tokens) will still be recorded for observability.

## Bring Your Own Key (BYOK)

TokenBee is a **stateless** gateway. We do not store your LLM provider API keys in our database. You pass your provider key (OpenAI, Anthropic, etc.) through the SDK's `llmKey` parameter. The SDK sends this in the `X-LLM-Key` header, allowing the TokenBee proxy to forward requests to the provider on your behalf while you maintain full control over your billing and security.

## License

MIT
