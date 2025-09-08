import { getCurrentFile } from './utils/common.js';
import { sendRequest } from './utils/apiService.js';

export function registerCsharpProvider() {
    // Monaco Editor 已内置 C# 语言支持
    // 完全使用原生语法高亮，不添加任何增强着色

    monaco.languages.registerCompletionItemProvider('csharp', {
        triggerCharacters: [".", " "],
        provideCompletionItems: async (model, position) => {
            let suggestions = [];

            const file = getCurrentFile();
            const packages = file?.nugetConfig?.packages || [];

            let request = {
                Code: model.getValue(),
                Position: model.getOffsetAt(position),
                Packages: packages.map(p => ({
                    Id: p.id,
                    Version: p.version
                }))
            }

            try {
                const { data } = await sendRequest("complete", request);
                for (let elem of data) {
                    suggestions.push({
                        label: {
                            label: elem.suggestion,
                            description: elem.description
                        },
                        kind: monaco.languages.CompletionItemKind.Function,
                        insertText: elem.suggestion
                    });
                }
            } catch (error) {
                console.error('Completion error:', error);
            }

            return { suggestions };
        }
    });

    monaco.languages.registerSignatureHelpProvider('csharp', {
        signatureHelpTriggerCharacters: ["("],
        signatureHelpRetriggerCharacters: [","],
        provideSignatureHelp: async (model, position) => {
            const file = getCurrentFile();
            const packages = file?.nugetConfig?.packages || [];

            let request = {
                Code: model.getValue(),
                Position: model.getOffsetAt(position),
                Packages: packages.map(p => ({
                    Id: p.id,
                    Version: p.version
                }))
            }

            try {
                const { data } = await sendRequest("signature", request);
                if (!data) return;

                let signatures = [];
                for (let signature of data.signatures) {
                    let params = [];
                    for (let param of signature.parameters) {
                        params.push({
                            label: param.label,
                            documentation: param.documentation ?? ""
                        });
                    }

                    signatures.push({
                        label: signature.label,
                        documentation: signature.documentation ?? "",
                        parameters: params,
                    });
                }

                let signatureHelp = {
                    signatures,
                    activeParameter: data.activeParameter,
                    activeSignature: data.activeSignature
                };

                return {
                    value: signatureHelp,
                    dispose: () => { }
                };
            } catch (error) {
                console.error('Signature help error:', error);
                return null;
            }
        }
    });

    monaco.languages.registerHoverProvider('csharp', {
        provideHover: async function (model, position) {
            const file = getCurrentFile();
            const packages = file?.nugetConfig?.packages || [];

            let request = {
                Code: model.getValue(),
                Position: model.getOffsetAt(position),
                Packages: packages.map(p => ({
                    Id: p.id,
                    Version: p.version
                }))
            }

            try {
                const { data } = await sendRequest("hover", request);
                if (!data) return null;

                const posStart = model.getPositionAt(data.offsetFrom);
                const posEnd = model.getPositionAt(data.offsetTo);

                return {
                    range: new monaco.Range(posStart.lineNumber, posStart.column, posEnd.lineNumber, posEnd.column),
                    contents: [
                        { value: data.information }
                    ]
                };
            } catch (error) {
                console.error('Hover error:', error);
                return null;
            }
        }
    });

    monaco.editor.onDidCreateModel(function (model) {
        async function validate() {
            const file = getCurrentFile();
            const packages = file?.nugetConfig?.packages || [];

            let request = {
                Code: model.getValue(),
                Packages: packages.map(p => ({
                    Id: p.id,
                    Version: p.version
                }))
            }

            try {
                const { data } = await sendRequest("codeCheck", request);
                let markers = [];

                for (let elem of data) {
                    const posStart = model.getPositionAt(elem.offsetFrom);
                    const posEnd = model.getPositionAt(elem.offsetTo);
                    markers.push({
                        severity: elem.severity,
                        startLineNumber: posStart.lineNumber,
                        startColumn: posStart.column,
                        endLineNumber: posEnd.lineNumber,
                        endColumn: posEnd.column,
                        message: elem.message,
                        code: elem.Id
                    });
                }

                monaco.editor.setModelMarkers(model, 'csharp', markers);
            } catch (error) {
                console.error('Validation error:', error);
            }
        }

        let handle = null;
        model.onDidChangeContent(() => {
            monaco.editor.setModelMarkers(model, 'csharp', []);
            clearTimeout(handle);
            handle = setTimeout(() => validate(), 500);
        });
        validate();
    });

    monaco.languages.registerDocumentFormattingEditProvider('csharp', {
        async provideDocumentFormattingEdits(model) {
            try {
                const sourceCode = model.getValue();
                const { data } = await sendRequest('format', {
                    SourceCode: sourceCode
                });

                return [{
                    range: {
                        startLineNumber: 1,
                        startColumn: 1,
                        endLineNumber: model.getLineCount(),
                        endColumn: model.getLineMaxColumn(model.getLineCount())
                    },
                    text: data.data
                }];
            } catch (error) {
                console.error('Format error:', error);
                return [];
            }
        }
    });

    monaco.languages.registerDefinitionProvider('csharp', {
        async provideDefinition(model, position) {
            const file = getCurrentFile();
            const packages = file?.nugetConfig?.packages || [];

            let request = {
                Code: model.getValue(),
                Position: model.getOffsetAt(position),
                Packages: packages.map(p => ({
                    Id: p.id,
                    Version: p.version
                }))
            };

            try {
                const { data } = await sendRequest("definition", request);
                if (!data || !data.locations || data.locations.length === 0) {
                    return [];
                }

                return data.locations.map(location => ({
                    uri: model.uri,
                    range: new monaco.Range(
                        location.range.startPosition.lineNumber,
                        location.range.startPosition.column,
                        location.range.endPosition.lineNumber,
                        location.range.endPosition.column
                    )
                }));
            } catch (error) {
                console.error('Definition error:', error);
                return [];
            }
        }
    });
}