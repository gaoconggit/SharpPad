import { getCurrentFile } from './utils/common.js';
import { sendRequest } from './utils/apiService.js';

export function registerCsharpProvider() {
    monaco.languages.register({ id: 'csharp' });

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
                            label: elem.Suggestion,
                            description: elem.Description
                        },
                        kind: monaco.languages.CompletionItemKind.Function,
                        insertText: elem.Suggestion
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
                for (let signature of data.Signatures) {
                    let params = [];
                    for (let param of signature.Parameters) {
                        params.push({
                            label: param.Label,
                            documentation: param.Documentation ?? ""
                        });
                    }

                    signatures.push({
                        label: signature.Label,
                        documentation: signature.Documentation ?? "",
                        parameters: params,
                    });
                }

                let signatureHelp = {
                    signatures,
                    activeParameter: data.ActiveParameter,
                    activeSignature: data.ActiveSignature
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

                const posStart = model.getPositionAt(data.OffsetFrom);
                const posEnd = model.getPositionAt(data.OffsetTo);

                return {
                    range: new monaco.Range(posStart.lineNumber, posStart.column, posEnd.lineNumber, posEnd.column),
                    contents: [
                        { value: data.Information }
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
                    const posStart = model.getPositionAt(elem.OffsetFrom);
                    const posEnd = model.getPositionAt(elem.OffsetTo);
                    markers.push({
                        severity: elem.Severity,
                        startLineNumber: posStart.lineNumber,
                        startColumn: posStart.column,
                        endLineNumber: posEnd.lineNumber,
                        endColumn: posEnd.column,
                        message: elem.Message,
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
}