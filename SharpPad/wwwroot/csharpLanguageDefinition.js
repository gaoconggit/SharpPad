// Custom C# Language Definition - Fixes string highlighting issues with URLs
// Specifically addresses the problem where https:// inside strings affects .Dump() highlighting

export function overrideCSharpLanguage() {
    // Enhanced C# language definition that fixes URL highlighting in strings
    const enhancedCSharpLanguage = {
        defaultToken: '',
        tokenPostfix: '.cs',

        keywords: [
            'extern', 'alias', 'using', 'bool', 'decimal', 'sbyte', 'byte', 'short',
            'ushort', 'int', 'uint', 'long', 'ulong', 'char', 'float', 'double',
            'object', 'dynamic', 'string', 'assembly', 'is', 'as', 'ref', 'out',
            'this', 'base', 'new', 'typeof', 'void', 'checked', 'unchecked',
            'default', 'delegate', 'var', 'const', 'if', 'else', 'switch', 'case',
            'while', 'do', 'for', 'foreach', 'in', 'break', 'continue', 'goto',
            'return', 'throw', 'try', 'catch', 'finally', 'lock', 'yield', 'from',
            'let', 'where', 'join', 'on', 'equals', 'into', 'orderby', 'ascending',
            'descending', 'select', 'group', 'by', 'namespace', 'partial', 'class',
            'field', 'event', 'method', 'param', 'public', 'protected', 'internal',
            'private', 'abstract', 'sealed', 'static', 'struct', 'readonly',
            'volatile', 'virtual', 'override', 'params', 'get', 'set', 'add',
            'remove', 'operator', 'true', 'false', 'implicit', 'explicit',
            'interface', 'enum', 'null', 'async', 'await', 'fixed', 'sizeof',
            'stackalloc', 'unsafe', 'nameof', 'when'
        ],

        namespaceFollows: ['namespace', 'using'],
        parenFollows: ['if', 'for', 'while', 'switch', 'foreach', 'using', 'catch', 'when'],
        operators: [
            '=', '??', '||', '&&', '|', '^', '&', '==', '!=', '<=', '>=', '<<',
            '+', '-', '*', '/', '%', '!', '~', '++', '--', '+=', '-=', '*=',
            '/=', '%=', '&=', '|=', '^=', '<<=', '>>=', '>>', '=>'
        ],

        symbols: /[=><!~?:&|+\-*\/\^%]+/,
        escapes: /\\(?:[abfnrtv\\"']|x[0-9A-Fa-f]{1,4}|u[0-9A-Fa-f]{4}|U[0-9A-Fa-f]{8})/,

        tokenizer: {
            root: [
                // Identifiers and keywords - MUST be first to properly tokenize code before comments
                [/\@?[a-zA-Z_]\w*/, {
                    cases: {
                        '@namespaceFollows': { token: 'keyword.$0', next: '@namespace' },
                        '@keywords': { token: 'keyword.$0', next: '@qualified' },
                        '@default': { token: 'identifier', next: '@qualified' }
                    }
                }],

                // Whitespace (includes comments)
                { include: '@whitespace' },

                // Brackets
                [/}/, {
                    cases: {
                        '$S2==interpolatedstring': { token: 'string.quote', next: '@pop' },
                        '$S2==litinterpstring': { token: 'string.quote', next: '@pop' },
                        '@default': '@brackets'
                    }
                }],
                [/[{}()\[\]]/, '@brackets'],
                [/[<>](?!@symbols)/, '@brackets'],

                // Operators
                [/@symbols/, {
                    cases: {
                        '@operators': 'delimiter',
                        '@default': ''
                    }
                }],

                // Numbers
                [/[0-9_]*\.[0-9_]+([eE][\-+]?\d+)?[fFdD]?/, 'number.float'],
                [/0[xX][0-9a-fA-F_]+/, 'number.hex'],
                [/0[bB][01_]+/, 'number.hex'],
                [/[0-9_]+/, 'number'],

                // Delimiters
                [/[;,.]/, 'delimiter'],

                // String literals - placed after basic tokens to prevent conflicts
                [/"([^"\\]|\\.)*$/, 'string.invalid'],
                [/"/, { token: 'string.quote', next: '@string' }],
                [/\$\@"/, { token: 'string.quote', next: '@litinterpstring' }],
                [/\@"/, { token: 'string.quote', next: '@litstring' }],
                [/\$"/, { token: 'string.quote', next: '@interpolatedstring' }],

                // Character literals
                [/'[^\\']'/, 'string'],
                [/(')(@escapes)(')/, ['string', 'string.escape', 'string']],
                [/'/, 'string.invalid']
            ],

            qualified: [
                [/[a-zA-Z_][\w]*/, {
                    cases: {
                        '@keywords': { token: 'keyword.$0' },
                        '@default': 'identifier'
                    }
                }],
                [/\./, 'delimiter'],
                ['', '', '@pop']
            ],

            namespace: [
                { include: '@whitespace' },
                [/[A-Z]\w*/, 'namespace'],
                [/[\.=]/, 'delimiter'],
                ['', '', '@pop']
            ],

            comment: [
                [/[^\/*]+/, 'comment'],
                ['\\*/', 'comment', '@pop'],
                [/[\/*]/, 'comment']
            ],

            // String state - NO comment processing allowed here
            string: [
                [/[^\\"]+/, 'string'],  // Everything inside string is just string content
                [/@escapes/, 'string.escape'],
                [/\\./, 'string.escape.invalid'],
                [/"/, { token: 'string.quote', next: '@pop' }]
            ],

            litstring: [
                [/[^"]+/, 'string'],  // Everything inside literal string is just string content
                [/""/, 'string.escape'],
                [/"/, { token: 'string.quote', next: '@pop' }]
            ],

            litinterpstring: [
                [/[^"{]+/, 'string'],
                [/""/, 'string.escape'],
                [/{{/, 'string.escape'],
                [/}}/, 'string.escape'],
                [/{/, { token: 'string.quote', next: 'root.litinterpstring' }],
                [/"/, { token: 'string.quote', next: '@pop' }]
            ],

            interpolatedstring: [
                [/[^\\"{]+/, 'string'],
                [/@escapes/, 'string.escape'],
                [/\\./, 'string.escape.invalid'],
                [/{{/, 'string.escape'],
                [/}}/, 'string.escape'],
                [/{/, { token: 'string.quote', next: 'root.interpolatedstring' }],
                [/"/, { token: 'string.quote', next: '@pop' }]
            ],

            // Whitespace handling - comments only processed when not in string context
            whitespace: [
                [/^[ \t\v\f]*#((r)|(load))(?=\s)/, 'directive.csx'],
                [/^[ \t\v\f]*#\w.*$/, 'namespace.cpp'],
                [/[ \t\v\f\r\n]+/, ''],
                [/\/\*/, 'comment', '@comment'],
                [/\/\/.*$/, 'comment']
            ]
        }
    };

    // Override the existing C# language tokenizer
    const languageId = 'csharp';
    let overrideApplied = false;

    const applyOverride = () => {
        if (!globalThis.monaco?.languages?.setMonarchTokensProvider) {
            return false;
        }

        try {
            globalThis.monaco.languages.setMonarchTokensProvider(languageId, enhancedCSharpLanguage);
            if (!overrideApplied) {
                overrideApplied = true;
                console.log('C# language tokenizer successfully overridden - fixed string URL highlighting');
            }
            return true;
        } catch (error) {
            console.error('Failed to override C# language tokenizer:', error);
            return false;
        }
    };

    const scheduleOverride = (delay = 0, attemptsLeft = 10) => {
        if (attemptsLeft <= 0) {
            return;
        }

        setTimeout(() => {
            const applied = applyOverride();
            if (!applied) {
                scheduleOverride(Math.min(delay + 50, 250), attemptsLeft - 1);
            }
        }, delay);
    };

    try {
        if (globalThis.monaco?.languages?.getLanguages?.().some(lang => lang.id === languageId)) {
            scheduleOverride();
        } else {
            scheduleOverride(25);
        }
    } catch (error) {
        console.warn('Unable to verify Monaco languages for override immediately:', error);
        scheduleOverride();
    }

    if (globalThis.monaco?.languages?.onLanguage) {
        globalThis.monaco.languages.onLanguage(languageId, () => {
            scheduleOverride();
            scheduleOverride(50);
            scheduleOverride(200);
        });
    } else {
        // Fallback: keep trying until Monaco exposes the language APIs
        scheduleOverride();
    }
}


