import {Copilot, logger} from './index.core.mjs';
var k = n => !n || n.length === 0 ? "" : n.length === 1 ? n[0] : `${n.slice(0, -1).join(", ")} and ${n.slice(-1)}`;
var S = (n, e, o={}) => {
    if (e <= 0)
        return "";
    let t = n.split(`
`)
      , r = t.length;
    if (e >= r)
        return n;
    if (o.truncateDirection === "keepEnd") {
        let s = t.slice(-e);
        return s.every(l => l === "") ? `
`.repeat(e) : s.join(`
`)
    }
    let i = t.slice(0, e);
    return i.every(s => s === "") ? `
`.repeat(e) : i.join(`
`)
}
;
var re = "<|developer_cursor_is_here|>"
  , B = n => ({
    instruction: ie(),
    context: se(n),
    fileContent: ae(n)
})
  , ie = () => "Provide concise and readable code completions that are syntactically and logically accurate, and seamlessly integrate with the existing context. Output only the raw code to be inserted at the cursor location without any additional text, comments, or text before or after the cursor."
  , se = n => {
    let {technologies: e=[], filename: o, relatedFiles: t=[], language: r} = n
      , i = k([r, ...e].filter(a => !!a))
      , s = t.length === 0 ? "" : t.map( ({path: a, content: p}) => `### ${a}
${p}`).join(`

`)
      , l = [i ? `Technology stack: ${i}` : "", `File: ${o || "unknown"}`].filter(Boolean).join(`
`);
    return `${s ? `${s}

` : ""}${l}`
}
  , ae = n => {
    let {textBeforeCursor: e, textAfterCursor: o} = n;
    return `**Current code:**
\`\`\`
${e}${re}${o}
\`\`\``
}
;
var b = class extends Copilot {
    async complete(e) {
        let {body: o, options: t} = e
          , {customPrompt: r, aiRequestHandler: i} = t ?? {}
          , {completionMetadata: s} = o
          , {text: l, raw: a, error: p} = await this.makeAIRequest(s, {
            customPrompt: r,
            aiRequestHandler: i
        });
        return {
            completion: l,
            raw: a,
            error: p
        }
    }
    getDefaultPrompt(e) {
        return B(e)
    }
}
;
var U = 100
  , j = true
  , T = "onIdle"
  , K = true
  , H = 120
  , V = 400
  , $ = 0;
var f = (n, e) => e.getValueInRange({
    startLineNumber: 1,
    startColumn: 1,
    endLineNumber: n.lineNumber,
    endColumn: n.column
})
  , W = (n, e) => e.getValueInRange({
    startLineNumber: n.lineNumber,
    startColumn: n.column,
    endLineNumber: e.getLineCount(),
    endColumn: e.getLineMaxColumn(e.getLineCount())
})
  , z = n => n.getValue();
var v = class {
    constructor(e) {
        this.capacity = e;
        this.head = 0;
        this.tail = 0;
        this.size = 0;
        this.buffer = new Array(e);
    }
    enqueue(e) {
        let o;
        return this.size === this.capacity && (o = this.dequeue()),
        this.buffer[this.tail] = e,
        this.tail = (this.tail + 1) % this.capacity,
        this.size++,
        o
    }
    dequeue() {
        if (this.size === 0)
            return;
        let e = this.buffer[this.head];
        return this.buffer[this.head] = void 0,
        this.head = (this.head + 1) % this.capacity,
        this.size--,
        e
    }
    getAll() {
        return this.buffer.filter(e => e !== void 0)
    }
    clear() {
        this.buffer = new Array(this.capacity),
        this.head = 0,
        this.tail = 0,
        this.size = 0;
    }
    getSize() {
        return this.size
    }
    isEmpty() {
        return this.size === 0
    }
    isFull() {
        return this.size === this.capacity
    }
}
;
var O = class O {
    constructor() {
        this.cache = new v(O.MAX_CACHE_SIZE);
    }
    get(e, o) {
        return this.cache.getAll().filter(t => this.isValidCacheItem(t, e, o))
    }
    add(e) {
        e.completion.trim() && this.cache.enqueue(e);
    }
    clear() {
        this.cache.clear();
    }
    isValidCacheItem(e, o, t) {
        let r = e.textBeforeCursor.trim()
          , i = f(o, t)
          , s = i
          , l = t.getLineContent(o.lineNumber);
        if (o.column === l.length + 1 && o.lineNumber < t.getLineCount()) {
            let p = t.getLineContent(o.lineNumber + 1);
            s = `${i}
${p}`;
        }
        if (!(s.trim().includes(r) || r.includes(s.trim())))
            return false;
        let a = t.getValueInRange(e.range);
        return this.isPartialMatch(a, e.completion) ? this.isPositionValid(e, o) : false
    }
    isPartialMatch(e, o) {
        let t = e.trim()
          , r = o.trim();
        return r.startsWith(t) || t.startsWith(r)
    }
    isPositionValid(e, o) {
        let {range: t} = e
          , {startLineNumber: r, startColumn: i, endLineNumber: s, endColumn: l} = t
          , {lineNumber: a, column: p} = o;
        return a < r || a > s ? false : r === s ? p >= i - 1 && p <= l + 1 : a === r ? p >= i - 1 : a === s ? p <= l + 1 : true
    }
}
;
O.MAX_CACHE_SIZE = 20;
var P = O;
var M = class {
    constructor(e) {
        this.formattedCompletion = "";
        this.formattedCompletion = e;
    }
    setCompletion(e) {
        return this.formattedCompletion = e,
        this
    }
    removeInvalidLineBreaks() {
        return this.formattedCompletion = this.formattedCompletion.trimEnd(),
        this
    }
    removeMarkdownCodeSyntax() {
        return this.formattedCompletion = this.removeMarkdownCodeBlocks(this.formattedCompletion),
        this
    }
    removeMarkdownCodeBlocks(e) {
        let o = e.split(`
`)
          , t = []
          , r = false;
        for (let i = 0; i < o.length; i++) {
            let s = o[i]
              , l = s.trim().startsWith("```");
            if (l && !r) {
                r = true;
                continue
            }
            if (l && r) {
                r = false;
                continue
            }
            t.push(s);
        }
        return t.join(`
`)
    }
    removeExcessiveNewlines() {
        return this.formattedCompletion = this.formattedCompletion.replace(/\n{3,}/g, `

`),
        this
    }
    build() {
        return this.formattedCompletion
    }
}
;
var I = class {
    findOverlaps(e, o, t) {
        if (!e)
            return {
                startOverlapLength: 0,
                maxOverlapLength: 0
            };
        let r = e.length
          , i = o.length
          , s = t.length
          , l = 0
          , a = 0
          , p = 0
          , d = Math.min(r, i);
        for (let m = 1; m <= d; m++) {
            let u = e.substring(0, m)
              , C = o.slice(-m);
            u === C && (p = m);
        }
        let c = Math.min(r, s);
        for (let m = 0; m < c && e[m] === t[m]; m++)
            l++;
        for (let m = 1; m <= c; m++)
            e.slice(-m) === t.slice(0, m) && (a = m);
        let g = Math.max(l, a);
        if (g === 0) {
            for (let m = 1; m < r; m++)
                if (t.startsWith(e.substring(m))) {
                    g = r - m;
                    break
                }
        }
        return {
            startOverlapLength: p,
            maxOverlapLength: g
        }
    }
}
;
var L = class {
    constructor(e) {
        this.monaco = e;
        this.textOverlapCalculator = new I;
    }
    computeInsertionRange(e, o, t) {
        if (!o)
            return this.createEmptyRange(e);
        let r = t.getOffsetAt(e)
          , i = t.getValue().substring(0, r)
          , s = t.getValue().substring(r);
        if (r >= t.getValue().length)
            return this.createEmptyRange(e);
        if (s.length === 0)
            return this.createEmptyRange(e);
        let {startOverlapLength: l, maxOverlapLength: a} = this.textOverlapCalculator.findOverlaps(o, i, s)
          , p = l > 0 ? t.getPositionAt(r - l) : e
          , d = r + a
          , c = t.getPositionAt(d);
        return new this.monaco.Range(p.lineNumber,p.column,c.lineNumber,c.column)
    }
    computeCacheRange(e, o) {
        let t = e.lineNumber
          , r = e.column
          , i = o.split(`
`)
          , s = i.length - 1
          , l = t + s
          , a = s === 0 ? r + i[0].length : i[s].length + 1;
        return new this.monaco.Range(t,r,l,a)
    }
    createEmptyRange(e) {
        return new this.monaco.Range(e.lineNumber,e.column,e.lineNumber,e.column)
    }
}
;
var G = async n => {
    let {endpoint: e, body: o} = n
      , t = await fetch(e, {
        method: "POST",
        headers: {
            "Content-Type": "application/json"
        },
        body: JSON.stringify(o)
    });
    if (!t.ok)
        throw new Error(`Error while fetching completion item: ${t.statusText}`);
    let {completion: r, error: i} = await t.json();
    return {
        completion: r,
        error: i
    }
}
  , X = ({pos: n, mdl: e, options: o}) => {
    let {filename: t, language: r, technologies: i, relatedFiles: s, maxContextLines: l=U} = o
      , p = s && s.length > 0 ? 3 : 2
      , d = l ? Math.floor(l / p) : void 0
      , c = (y, R, F) => {
        let x = y(n, e);
        return R ? S(x, R, F) : x
    }
      , g = (y, R) => !y || !R ? y : y.map( ({content: F, ...x}) => ({
        ...x,
        content: S(F, R)
    }))
      , m = c(f, d, {
        truncateDirection: "keepEnd"
    })
      , u = c(W, d, {
        truncateDirection: "keepStart"
    })
      , C = g(s, d);
    return {
        filename: t,
        language: r,
        technologies: i,
        relatedFiles: C,
        textBeforeCursor: m,
        textAfterCursor: u,
        cursorPosition: n
    }
}
;
var Y = (n, e=300) => {
    let o = null
      , t = null
      , r = (...i) => {
        if (t)
            return t.args = i,
            t.promise;
        let s, l, a = new Promise( (p, d) => {
            s = p,
            l = d;
        }
        );
        return t = {
            args: i,
            promise: a,
            resolve: s,
            reject: l
        },
        o && (clearTimeout(o),
        o = null),
        o = setTimeout(async () => {
            let p = t;
            if (p) {
                t = null,
                o = null;
                try {
                    let d = await n(...p.args);
                    p.resolve(d);
                } catch (d) {
                    p.reject(d);
                }
            }
        }
        , e),
        a
    }
    ;
    return r.cancel = () => {
        o && (clearTimeout(o),
        o = null),
        t && (t.reject(new Error("Cancelled")),
        t = null);
    }
    ,
    r
}
;
var Z = n => typeof n == "string" ? n === "Cancelled" || n === "AbortError" : n instanceof Error ? n.message === "Cancelled" || n.name === "AbortError" : false;
var h = n => ({
    items: n,
    enableForwardStability: true
});
var A = new P
  , J = async ({monaco: n, mdl: e, pos: o, token: t, isCompletionAccepted: r, options: i}) => {
    let {trigger: s=T, enableCaching: l=j, allowFollowUpCompletions: a=K, onError: p, requestHandler: d} = i;
    if (l && !r) {
        let c = A.get(o, e).map(g => ({
            insertText: g.completion,
            range: g.range
        }));
        if (c.length > 0)
            return h(c)
    }
    if (t.isCancellationRequested || !a && r)
        return h([]);
    try {
        let c = Y(async u => {
            i.onCompletionRequested?.(u);
            let C;
            if (d)
                C = await d(u);
            else if (i.endpoint)
                C = await G({
                    endpoint: i.endpoint,
                    ...u
                });
            else
                throw new Error('No endpoint specified for completion request. Please set the "endpoint" option in registerCompletion, or provide a custom requestHandler.');
            if (C.error)
                throw new Error(C.error);
            return i.onCompletionRequestFinished?.(u, C),
            C
        }
        , {
            onTyping: H,
            onIdle: V,
            onDemand: $
        }[s]);
        t.onCancellationRequested( () => {
            c.cancel();
        }
        );
        let g = X({
            pos: o,
            mdl: e,
            options: i
        })
          , {completion: m} = await c({
            body: {
                completionMetadata: g
            }
        });
        if (m) {
            let u = new M(m).removeMarkdownCodeSyntax().removeExcessiveNewlines().removeInvalidLineBreaks().build()
              , C = new L(n);
            return l && A.add({
                completion: u,
                range: C.computeCacheRange(o, u),
                textBeforeCursor: f(o, e)
            }),
            h([{
                insertText: u,
                range: C.computeInsertionRange(o, u, e)
            }])
        }
    } catch (c) {
        if (Z(c))
            return h([]);
        p ? p(c) : logger.warn("Cannot provide completion", c);
    }
    return h([])
}
;
var w = new WeakMap
  , E = n => w.get(n)
  , Q = (n, e) => {
    w.set(n, e);
}
  , D = n => {
    w.delete(n);
}
  , ee = n => ({
    isCompletionAccepted: false,
    isCompletionVisible: false,
    isExplicitlyTriggered: false,
    hasRejectedCurrentCompletion: false,
    options: n
})
  , te = (n, e) => {
    let o = E(n);
    !o || !o.options || (o.options = {
        ...o.options,
        ...e
    });
}
;
var oe = (n, e, o) => {
    let t = E(e);
    return t ? n.languages.registerInlineCompletionsProvider(o.language, {
        provideInlineCompletions: (r, i, s, l) => {
            if (r !== e.getModel())
                return {
                    items: []
                };
            let a = t.options || o;
            if (!(a.trigger === "onDemand" && !t.isExplicitlyTriggered || a.triggerIf && !a.triggerIf({
                text: z(e),
                position: i,
                triggerType: a.trigger ?? T
            })))
                return J({
                    monaco: n,
                    mdl: r,
                    pos: i,
                    token: l,
                    isCompletionAccepted: t.isCompletionAccepted,
                    options: a
                })
        }
        ,
        handleItemDidShow: (r, i, s) => {
            if (t.isExplicitlyTriggered = false,
            t.hasRejectedCurrentCompletion = false,
            t.isCompletionAccepted)
                return;
            t.isCompletionVisible = true,
            (t.options || o).onCompletionShown?.(s, i.range);
        }
        ,
        freeInlineCompletions: () => {}
    }) : null
}
;
var me = {
    TAB: (n, e) => e.keyCode === n.KeyCode.Tab,
    CMD_RIGHT_ARROW: (n, e) => e.keyCode === n.KeyCode.RightArrow && e.metaKey
}
  , N = class {
    constructor(e, o, t) {
        this.monaco = e;
        this.state = o;
        this.initialOptions = t;
    }
    handleKeyEvent(e) {
        let o = this.state.options || this.initialOptions
          , t = {
            monaco: this.monaco,
            event: e,
            state: this.state,
            options: o
        };
        this.handleCompletionAcceptance(t),
        this.handleCompletionRejection(t);
    }
    handleCompletionAcceptance(e) {
        return e.state.isCompletionVisible && this.isAcceptanceKey(e.event) ? (e.options.onCompletionAccepted?.(),
        e.state.isCompletionAccepted = true,
        e.state.isCompletionVisible = false,
        true) : (e.state.isCompletionAccepted = false,
        false)
    }
    handleCompletionRejection(e) {
        return this.shouldRejectCompletion(e) ? (e.options.onCompletionRejected?.(),
        e.state.hasRejectedCurrentCompletion = true,
        true) : false
    }
    shouldRejectCompletion(e) {
        return e.state.isCompletionVisible && !e.state.hasRejectedCurrentCompletion && !e.state.isCompletionAccepted && !this.isAcceptanceKey(e.event)
    }
    isAcceptanceKey(e) {
        return Object.values(me).some(o => o(this.monaco, e))
    }
}
  , ne = (n, e, o, t) => {
    let r = new N(n,o,t);
    return e.onKeyDown(i => r.handleKeyEvent(i))
}
;
var ce = (n, e, o) => {
    let t = [];
    Q(e, ee(o)),
    e.updateOptions({
        inlineSuggest: {
            enabled: true
        }
    });
    try {
        let r = E(e);
        if (!r)
            return logger.warn("Completion is not registered properly. State not found."),
            ue();
        let i = oe(n, e, o);
        i && t.push(i);
        let s = ne(n, e, r, o);
        return t.push(s),
        {
            deregister: () => {
                for (let a of t)
                    a.dispose();
                A.clear(),
                D(e);
            }
            ,
            trigger: () => de(e),
            updateOptions: a => {
                te(e, a(r.options || o));
            }
        }
    } catch (r) {
        return o.onError ? o.onError(r) : logger.report(r),
        {
            deregister: () => {
                for (let i of t)
                    i.dispose();
                D(e);
            }
            ,
            trigger: () => {}
            ,
            updateOptions: () => {}
        }
    }
}
  , de = n => {
    let e = E(n);
    if (!e) {
        logger.warn("Completion is not registered. Use `registerCompletion` to register completion first.");
        return
    }
    e.isExplicitlyTriggered = true,
    n.trigger("keyboard", "editor.action.inlineSuggest.trigger", {});
}
  , ue = () => ({
    deregister: () => {}
    ,
    trigger: () => {}
    ,
    updateOptions: () => {}
});
var ut = b;
export {b as CompletionCopilot, ut as Copilot, ce as registerCompletion};
