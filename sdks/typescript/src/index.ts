export enum CompressionRate {
  Low = "0.75",
  Medium = "0.5",
  High = "0.33",
  Extreme = "0.2"
}

export enum TokenBeeModel {
  // OpenAI
  OpenAIGPT4_5 = "openai/gpt-4.5-preview",
  OpenAIGPT4o = "openai/gpt-4o",
  OpenAIGPT4oMini = "openai/gpt-4o-mini",
  OpenAIO1 = "openai/o1",
  OpenAIO1Mini = "openai/o1-mini",
  OpenAIO3Mini = "openai/o3-mini",

  // Anthropic
  AnthropicClaude3_7_Sonnet = "anthropic/claude-3-7-sonnet-latest",
  AnthropicClaude3_5_Sonnet = "anthropic/claude-3-5-sonnet-latest",
  AnthropicClaude3_5_Haiku = "anthropic/claude-3-5-haiku-latest",
  AnthropicClaude3_Opus = "anthropic/claude-3-opus-latest",

  // Google
  Gemini3_1_Pro = "google/gemini-3.1-pro",
  Gemini3_1_Flash = "google/gemini-3.1-flash",
  Gemini2_5_Pro = "google/gemini-2.5-pro",
  Gemini2Flash = "google/gemini-2.0-flash",
  Gemini2Pro = "google/gemini-2.0-pro-exp",
  Gemini1_5_Pro = "google/gemini-1.5-pro",


  // Mistral
  MistralLarge = "mistral/mistral-large-latest",
  MistralSmall = "mistral/mistral-small-latest",
  PixtralLarge = "mistral/pixtral-large-latest",
  MistralNemo = "mistral/open-mistral-nemo",

  // Perplexity
  PerplexitySonar = "perplexity/sonar",
  PerplexitySonarPro = "perplexity/sonar-pro",
  PerplexitySonarReasoning = "perplexity/sonar-reasoning",

  // Groq
  GroqLlama3_3_70b = "groq/llama-3.3-70b-versatile",
  GroqLlama3_1_8b = "groq/llama-3.1-8b-instant",
  GroqDeepSeekR1Distill = "groq/deepseek-r1-distill-llama-70b",

  // xAI
  XAIGrok3 = "xai/grok-3",
  XAIGrok2 = "xai/grok-2-1212",
  XAIGrok2Mini = "xai/grok-2-mini-1212",
}

export interface TokenBeeOptions {
  compression?: string;
  rate?: CompressionRate | string;
  model?: TokenBeeModel | string;
  provider?: string;
  privacy?: boolean;
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
      "X-TokenBee-Model": modelName,
      "X-TokenBee-Provider": provider
    };

    if (params.input.sessionId) headers["X-TB-Session-Id"] = params.input.sessionId;
    if (params.input.userId) headers["X-TB-User-Id"] = params.input.userId;

    const privacy = params.input.privacy !== undefined ? params.input.privacy : this.options.privacy;
    if (privacy !== undefined) {
      headers["X-TokenBee-Privacy"] = String(privacy);
    }

    const payload = { model: modelName, ...params.input };
    // Remove SDK-specific keys from the payload sent to the LLM
    delete payload.compression;
    delete payload.rate;
    delete payload.privacy;
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
