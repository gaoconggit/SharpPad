// 编辑器自动保存管理
export class AutoSaveManager {
    constructor({ editor, fileManager, delayMs = 1000 } = {}) {
        this.editor = editor;
        this.fileManager = fileManager;
        this.delayMs = delayMs;
        this.debounceTimer = null;
        this.lastSavedContent = new Map();

        this.registerListeners();
    }

    registerListeners() {
        if (!this.editor?.onDidChangeModelContent) {
            console.warn('AutoSaveManager: editor is not ready, auto-save skipped');
            return;
        }

        this.editor.onDidChangeModelContent(() => this.scheduleSave());
    }

    scheduleSave() {
        if (this.debounceTimer) {
            clearTimeout(this.debounceTimer);
        }

        this.debounceTimer = setTimeout(() => {
            this.debounceTimer = null;
            this.saveCurrentFile();
        }, this.delayMs);
    }

    saveCurrentFile() {
        const selectedFileId = document.querySelector('#fileListItems a.selected')?.getAttribute('data-file-id');
        if (!selectedFileId || !this.editor) {
            return;
        }

        const code = this.editor.getValue();
        const lastSaved = this.lastSavedContent.get(selectedFileId) ?? localStorage.getItem(`file_${selectedFileId}`);

        // 跳过未变更内容，避免不必要的写入
        if (lastSaved === code) {
            return;
        }

        try {
            const saved = this.fileManager?.saveFileToLocalStorage?.(selectedFileId, code, { silent: true });
            if (saved) {
                this.lastSavedContent.set(selectedFileId, code);
            }
        } catch (error) {
            console.error('自动保存失败:', error);
        }
    }

    dispose() {
        if (this.debounceTimer) {
            clearTimeout(this.debounceTimer);
            this.debounceTimer = null;
        }
    }
}
