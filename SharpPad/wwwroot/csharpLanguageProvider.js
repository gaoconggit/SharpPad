async function sendRequest(type, request) {
    let endPoint;
    switch (type) {
        case 'complete': endPoint = '/completion/complete'; break;
        case 'signature': endPoint = '/completion/signature'; break;
        case 'hover': endPoint = '/completion/hover'; break;
        case 'codeCheck': endPoint = '/completion/codeCheck'; break;
        case 'format': endPoint = '/completion/format'; break;
        case 'run': endPoint = '/completion/run'; break;
        case 'addPackages': endPoint = '/completion/addPackages'; break;
    }
    
    // 延迟超过1秒后才显示加载中样式
    const notification = document.getElementById('notification');
    const showNotificationDelay = 1000; // 1 second
    let showNotificationTimer = setTimeout(() => {
        notification.textContent = '处理中...';
        notification.style.backgroundColor = 'rgba(33, 150, 243, 0.9)';
        notification.style.display = 'block';
    }, showNotificationDelay);
    
    try {
        if (type === 'run') {
            // 使用 POST 请求发送代码和包信息
            const response = await fetch(endPoint, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(request)
            });

            return {
                reader: response.body.getReader(),
                showNotificationTimer
            };
        } else {
            const response = await axios.post(endPoint, JSON.stringify(request));
            clearTimeout(showNotificationTimer);
            notification.style.display = 'none';
            return response;
        }
    } catch (error) {
        clearTimeout(showNotificationTimer);
        notification.textContent = '请求失败: ' + error.message;
        notification.style.backgroundColor = 'rgba(244, 67, 54, 0.9)';
        notification.style.display = 'block';
        throw error;
    }
}

function registerCsharpProvider() {

    var assemblies = [
    ];

    monaco.languages.registerCompletionItemProvider('csharp', {
        triggerCharacters: [".", " "],
        provideCompletionItems: async (model, position) => {
            let suggestions = [];

            let request = {
                Code: model.getValue(),
                Position: model.getOffsetAt(position),
                Assemblies: assemblies
            }

            let resultQ = await sendRequest("complete", request);

            for (let elem of resultQ.data) {
                suggestions.push({
                    label: {
                        label: elem.Suggestion,
                        description: elem.Description
                    },
                    kind: monaco.languages.CompletionItemKind.Function,
                    insertText: elem.Suggestion
                });
            }

            return { suggestions: suggestions };
        }
    });

    monaco.languages.registerSignatureHelpProvider('csharp', {
        signatureHelpTriggerCharacters: ["("],
        signatureHelpRetriggerCharacters: [","],

        provideSignatureHelp: async (model, position, token, context) => {

            let request = {
                Code: model.getValue(),
                Position: model.getOffsetAt(position),
                Assemblies: assemblies
            }

            let resultQ = await sendRequest("signature", request);
            if (!resultQ.data) return;

            let signatures = [];
            for (let signature of resultQ.data.Signatures) {
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

            let signatureHelp = {};
            signatureHelp.signatures = signatures;
            signatureHelp.activeParameter = resultQ.data.ActiveParameter;
            signatureHelp.activeSignature = resultQ.data.ActiveSignature;

            return {
                value: signatureHelp,
                dispose: () => { }
            };
        }
    });


    monaco.languages.registerHoverProvider('csharp', {
        provideHover: async function (model, position) {

            let request = {
                Code: model.getValue(),
                Position: model.getOffsetAt(position),
                Assemblies: assemblies
            }

            let resultQ = await sendRequest("hover", request);

            if (resultQ.data) {
                posStart = model.getPositionAt(resultQ.data.OffsetFrom);
                posEnd = model.getPositionAt(resultQ.data.OffsetTo);

                return {
                    range: new monaco.Range(posStart.lineNumber, posStart.column, posEnd.lineNumber, posEnd.column),
                    contents: [
                        { value: resultQ.data.Information }
                    ]
                };
            }

            return null;
        }
    });

    monaco.editor.onDidCreateModel(function (model) {
        async function validate() {

            let request = {
                Code: model.getValue(),
                Assemblies: assemblies
            }

            let resultQ = await sendRequest("codeCheck", request)

            let markers = [];

            for (let elem of resultQ.data) {
                posStart = model.getPositionAt(elem.OffsetFrom);
                posEnd = model.getPositionAt(elem.OffsetTo);
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
        }

        var handle = null;
        model.onDidChangeContent(() => {
            monaco.editor.setModelMarkers(model, 'csharp', []);
            clearTimeout(handle);
            handle = setTimeout(() => validate(), 500);
        });
        validate();
    });

    monaco.languages.registerDocumentFormattingEditProvider('csharp', {
        provideDocumentFormattingEdits: async function (model, options, token) {
            var sourceCode = model.getValue();

            let request = {
                SourceCode: sourceCode
            }

            let formated = await sendRequest("format", request)

            //给出格式化后的代码
            return [{
                range: model.getFullModelRange(),
                text: formated.data.data
            }];
        }
    });

    /*monaco.languages.registerInlayHintsProvider('csharp', {
        displayName: 'test',
        provideInlayHints(model, range, token) {
            return {
                hints: [
                    {
                        label: "Test",
                        tooltip: "Tooltip",
                        position: { lineNumber: 3, column: 2},
                        kind: 2
                    }
                ],
                dispose: () => { }
            };
        }

    });*/

    /*monaco.languages.registerCodeActionProvider("csharp", {
        provideCodeActions: async (model, range, context, token) => {
            const actions = context.markers.map(error => {
                console.log(context, error);
                return {
                    title: `Example quick fix`,
                    diagnostics: [error],
                    kind: "quickfix",
                    edit: {
                        edits: [
                            {
                                resource: model.uri,
                                edits: [
                                    {
                                        range: error,
                                        text: "This text replaces the text with the error"
                                    }
                                ]
                            }
                        ]
                    },
                    isPreferred: true
                };
            });
            return {
                actions: actions,
                dispose: () => { }
            }
        }
    });*/
}