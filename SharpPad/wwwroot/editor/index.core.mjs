var y = 256
  , h = `

`
  , E = .1
  , g = .1
  , M = false;
var l = ["mistral"]
  , O = {
    codestral: "codestral-latest"
}
  , m = {
    mistral: ["codestral"]
}
  , R = {
    mistral: "https://api.mistral.ai/v1/fim/completions"
};
var a = class {
}
;
var p = class extends a {
    createEndpoint() {
        return R.mistral
    }
    createRequestBody(e, o, r) {
        return {
            model: O[e],
            prompt: `${o.context}
${o.instruction}
${r.textBeforeCursor}`,
            suffix: r.textAfterCursor,
            stream: false,
            top_p: .1,
            temperature: .1,
            stop: h,
            max_tokens: 256
        }
    }
    createHeaders(e) {
        return {
            "Content-Type": "application/json",
            Authorization: `Bearer ${e}`
        }
    }
    parseCompletion(e) {
        let o = e.choices?.[0]?.message.content;
        return o ? Array.isArray(o) ? o.filter(r => "text"in r).map(r => r.text).join("") : o : null
    }
}
;
var d = {
    mistral: new p
}
  , v = (t, e, o) => d[o].createEndpoint(t, e)
  , x = (t, e, o, r) => d[e].createRequestBody(t, o, r)
  , T = (t, e) => d[e].createHeaders(t)
  , A = (t, e) => d[e].parseCompletion(t);
var c = "\x1B[0m"
  , P = "\x1B[1m"
  , D = t => t instanceof Error ? t.message : typeof t == "string" ? t : "An unknown error occurred"
  , I = t => {
    let e = D(t)
      , o = `${P}[MONACOPILOT ERROR] ${e}${c}`;
    return console.error(o),
    {
        message: e
    }
}
  , k = (t, e) => {
    console.warn(`${P}[MONACOPILOT WARN] ${t}${e ? `
${D(e)}` : ""}${c}`);
}
  , L = (t, e, o) => console.warn(`${P}[MONACOPILOT DEPRECATED] "${t}" is deprecated${o ? ` in ${o}` : ""}. Please use "${e}" instead. It will be removed in a future version.${c}`)
  , w = {
    report: I,
    warn: k,
    warnDeprecated: L
};
var _ = async (t, e={}, o=2e4) => {
    let r = new AbortController
      , {signal: n} = r
      , i = setTimeout( () => {
        r.abort();
    }
    , o);
    try {
        return await fetch(t, {
            ...e,
            signal: n
        })
    } catch (s) {
        throw s instanceof DOMException && s.name === "AbortError" ? new Error(`Request timed out after ${o}ms`) : s
    } finally {
        clearTimeout(i);
    }
}
;
var u = t => !t || t.length === 0 ? "" : t.length === 1 ? t[0] : `${t.slice(0, -1).join(", ")} and ${t.slice(-1)}`;
var $ = (t, e) => {
    if (!t && typeof e.model != "function")
        throw new Error(e.provider ? `Please provide the ${e.provider} API key.` : "Please provide an API key.");
    if (!e || typeof e == "object" && Object.keys(e).length === 0)
        throw new Error('Please provide required Copilot options, such as "model" and "provider".')
}
  , b = (t, e) => {
    if (typeof t == "function" && e !== void 0)
        throw new Error("Provider should not be specified when using a custom model.");
    if (typeof t != "function" && (!e || !l.includes(e)))
        throw new Error(`Provider must be specified and supported when using built-in models. Please choose from: ${u(l)}`);
    if (typeof t == "string" && e !== void 0 && !m[e].includes(t))
        throw new Error(`Model "${t}" is not supported by the "${e}" provider. Supported models: ${u(m[e])}`)
}
  , f = {
    params: $,
    inputs: b
};
var C = class {
    constructor(e, o) {
        f.params(e, o),
        this.apiKey = e ?? "",
        this.provider = o.provider,
        this.model = o.model,
        f.inputs(this.model, this.provider);
    }
    generatePrompt(e, o) {
        let r = this.getDefaultPrompt(e);
        return o ? {
            ...r,
            ...o(e)
        } : r
    }
    async makeAIRequest(e, o={}) {
        try {
            let r = this.generatePrompt(e, o.customPrompt);
            if (this.isCustomModel())
                return this.model(r);
            {
                let {aiRequestHandler: n} = o
                  , i = await this.prepareRequest(r, e)
                  , s = await this.sendRequest(i.endpoint, i.requestBody, i.headers, n);
                return this.processResponse(s)
            }
        } catch (r) {
            return this.handleError(r)
        }
    }
    async prepareRequest(e, o) {
        if (!this.provider)
            throw new Error("Provider is required for non-custom models");
        return {
            endpoint: v(this.model, this.apiKey, this.provider),
            headers: T(this.apiKey, this.provider),
            requestBody: x(this.model, this.provider, e, o)
        }
    }
    processResponse(e) {
        if (!this.provider)
            throw new Error("Provider is required for non-custom models");
        return {
            text: A(e, this.provider),
            raw: e
        }
    }
    isCustomModel() {
        return typeof this.model == "function"
    }
    async sendRequest(e, o, r, n) {
        let i = {
            "Content-Type": "application/json",
            ...r
        };
        if (n)
            return n({
                endpoint: e,
                body: o,
                headers: i
            });
        let s = await _(e, {
            method: "POST",
            headers: i,
            body: JSON.stringify(o)
        });
        if (!s.ok)
            throw new Error(await s.text());
        return s.json()
    }
    handleError(e) {
        return {
            text: null,
            error: w.report(e).message
        }
    }
}
;
export {C as Copilot, y as DEFAULT_COPILOT_MAX_TOKENS, h as DEFAULT_COPILOT_STOP_SEQUENCE, M as DEFAULT_COPILOT_STREAM, E as DEFAULT_COPILOT_TEMPERATURE, g as DEFAULT_COPILOT_TOP_P, O as MODEL_IDS, l as PROVIDERS, R as PROVIDER_ENDPOINT_MAP, m as PROVIDER_MODEL_MAP, w as logger};
