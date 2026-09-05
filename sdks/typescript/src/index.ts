export enum CompressionRate {
  Low = "0.75",
  Medium = "0.5",
  High = "0.33",
  Extreme = "0.2"
}

export enum CompressionStrategy {
  Hive = "hive_v1",
  Smart = "smart_v1"
}

export enum TokenBeeContext {
  Auto = "auto",
  Conversation = "conversation",
  Document = "document",
  Agent = "agent",
  Code = "code"
}

export enum TokenBeeModel {
  // OpenAI — current GPT-5.6 / GPT-5 family
  OpenAIGPT5_6Sol = "openai/gpt-5.6-sol",
  OpenAIGPT5_6Terra = "openai/gpt-5.6-terra",
  OpenAIGPT5_6Luna = "openai/gpt-5.6-luna",
  OpenAIGPT5 = "openai/gpt-5",
  OpenAIGPT5Mini = "openai/gpt-5-mini",
  OpenAIGPT5Nano = "openai/gpt-5-nano",
  OpenAIGPT5_4 = "openai/gpt-5.4",
  OpenAIGPT5_4Mini = "openai/gpt-5.4-mini",
  OpenAIGPT5_4Nano = "openai/gpt-5.4-nano",
  OpenAIGPT4_1 = "openai/gpt-4.1",
  OpenAIGPT4_1Mini = "openai/gpt-4.1-mini",
  OpenAIGPT4_1Nano = "openai/gpt-4.1-nano",
  OpenAIGPT4o = "openai/gpt-4o",
  OpenAIGPT4oMini = "openai/gpt-4o-mini",
  OpenAIO3 = "openai/o3",
  OpenAIO3Mini = "openai/o3-mini",
  OpenAIO4Mini = "openai/o4-mini",
  OpenAIO1 = "openai/o1",
  OpenAIO1Mini = "openai/o1-mini",

  // Anthropic
  AnthropicClaudeSonnet4 = "anthropic/claude-sonnet-4-latest",
  AnthropicClaudeOpus4 = "anthropic/claude-opus-4-latest",
  AnthropicClaudeHaiku4 = "anthropic/claude-haiku-4-latest",
  AnthropicClaude3_7_Sonnet = "anthropic/claude-3-7-sonnet-latest",
  AnthropicClaude3_5_Sonnet = "anthropic/claude-3-5-sonnet-latest",
  AnthropicClaude3_5_Haiku = "anthropic/claude-3-5-haiku-latest",

  // Google
  Gemini2_5_Pro = "google/gemini-2.5-pro",
  Gemini2_5_Flash = "google/gemini-2.5-flash",
  Gemini2Flash = "google/gemini-2.0-flash",

  // Mistral
  MistralLarge = "mistral/mistral-large-latest",
  MistralSmall = "mistral/mistral-small-latest",
  PixtralLarge = "mistral/pixtral-large-latest",
  MistralNemo = "mistral/open-mistral-nemo",

  // Perplexity
  PerplexitySonar = "perplexity/sonar",
  PerplexitySonarPro = "perplexity/sonar-pro",
  PerplexitySonarReasoning = "perplexity/sonar-reasoning",

  // Groq (current — Aug 2026)
  // Note: model IDs after "groq/" are sent to Groq as-is (may include org prefix).
  GroqGptOss120b = "groq/openai/gpt-oss-120b",
  GroqGptOss20b = "groq/openai/gpt-oss-20b",
  GroqGptOssSafeguard20b = "groq/openai/gpt-oss-safeguard-20b",
  GroqQwen3_6_27b = "groq/qwen/qwen3.6-27b",
  GroqCompound = "groq/groq/compound",
  GroqCompoundMini = "groq/groq/compound-mini",

  // xAI
  XAIGrok3 = "xai/grok-3",
  XAIGrok2 = "xai/grok-2-1212",
  XAIGrok2Mini = "xai/grok-2-mini-1212",
}

export interface TokenBeeOptions {
  compression?: string;
  rate?: CompressionRate | string;
  strategy?: CompressionStrategy | string;
  context?: TokenBeeContext | string;
  model?: TokenBeeModel | string;
  provider?: string;
  capture?: boolean;
}

export class TokenBee {
  private apiKey: string;
  private llmKey: string;
  private options: TokenBeeOptions;
  private readonly baseUrl: string;
  constructor(config: { apiKey: string, llmKey: string, options?: TokenBeeOptions }) {
    this.apiKey = config.apiKey;
    this.llmKey = config.llmKey;
    this.options = config.options || {};
    this.baseUrl = "https://api.tokenbee.io/v1";
  }

  async send(params: { model: TokenBeeModel | string, input: any }) {
    const parts = params.model.split('/');
    const provider = parts.length > 1 ? parts[0] : (this.options.provider || "openai");
    const modelName = parts.length > 1 ? parts.slice(1).join('/') : parts[0];

    const headers: Record<string, string> = {
      "Authorization": `Bearer ${this.apiKey}`,
      "X-TokenBee-Key": this.apiKey,
      "X-TB-Key": this.apiKey, // Legacy/Alternative
      "X-LLM-Key": this.llmKey,
      "Content-Type": "application/json",
      "X-TokenBee-Compression": params.input.compression || this.options.compression || "auto",
      "X-TokenBee-Rate": params.input.rate || this.options.rate || CompressionRate.Medium,
      "X-TokenBee-Strategy": params.input.strategy || this.options.strategy || CompressionStrategy.Smart,
      "X-TokenBee-Context": params.input.context || this.options.context || TokenBeeContext.Auto,
      "X-TokenBee-Model": modelName,
      "X-TokenBee-Provider": provider
    };

    if (params.input.sessionId) headers["X-TB-Session-Id"] = params.input.sessionId;
    if (params.input.userId) headers["X-TB-User-Id"] = params.input.userId;

    const capture = params.input.capture !== undefined ? params.input.capture : this.options.capture;
    if (capture !== undefined) {
      headers["X-TokenBee-Capture"] = String(capture);
    }

    const payload = { model: modelName, ...params.input };
    // Remove SDK-specific keys from the payload sent to the LLM
    delete payload.compression;
    delete payload.rate;
    delete payload.strategy;
    delete payload.context;
    delete payload.capture;
    delete payload.sessionId;
    delete payload.userId;

    const response = await fetch(`${this.baseUrl}/chat/completions`, {
      method: "POST",
      headers,
      body: JSON.stringify(payload),
    });

    if (!response.ok) {
      const errorBody = await response.text();
      throw new Error(`TokenBee HTTP error: ${response.status} ${response.statusText} - ${errorBody}`);
    }
    return response.json();
  }
}
