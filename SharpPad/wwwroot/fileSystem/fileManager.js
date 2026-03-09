// 文件系统管理模块 - 基于后端文件系统的 VS Code 风格项目管理
import { showNotification, DEFAULT_CODE, PROJECT_TYPE_CHANGE_EVENT } from '../utils/common.js';
import { customPrompt, customConfirm } from '../utils/customPrompt.js';
import { fileListResizer } from './fileListResizer.js';

const API_BASE = '/api/fs';

class FileManager {
    constructor() {
        if (FileManager.instance) {
            return FileManager.instance;
        }
        FileManager.instance = this;

        this.fileListItems = document.getElementById('fileListItems');
        this.currentFilePath = null;  // 当前打开文件的相对路径
        this.workspaceRoot = '';
        this.treeData = [];
        this.expandedFolders = new Set(JSON.parse(localStorage.getItem('expandedFolders') || '[]'));
        this.initializeEventListeners();
    }

    static getInstance() {
        if (!FileManager.instance) {
            FileManager.instance = new FileManager();
        }
        return FileManager.instance;
    }

    initializeEventListeners() {
        const fileFilter = document.getElementById('fileFilter');
        if (fileFilter) {
            fileFilter.addEventListener('keyup', (e) => this.filterFiles(e.target.value));
        }

        const addFileBtn = document.getElementById('addFileBtn');
        if (addFileBtn) {
            addFileBtn.addEventListener('click', () => this.addFile());
        }

        const addFolderBtn = document.getElementById('addFolderBtn');
        if (addFolderBtn) {
            addFolderBtn.addEventListener('click', () => this.addFolder());
        }

        const toggleFileListBtn = document.getElementById('toggleFileList');
        if (toggleFileListBtn) {
            toggleFileListBtn.addEventListener('click', () => this.toggleFileList());
        }

        const restoreFileListBtn = document.querySelector('.restore-filelist');
        if (restoreFileListBtn) {
            restoreFileListBtn.addEventListener('click', () => this.toggleFileList());
        }

        const openFolderBtn = document.getElementById('openFolderBtn');
        if (openFolderBtn) {
            openFolderBtn.addEventListener('click', () => this.openFolder());
        }

        const refreshBtn = document.getElementById('refreshTreeBtn');
        if (refreshBtn) {
            refreshBtn.addEventListener('click', () => this.loadFileList());
        }

        this.initializeFileListState();
        this.initializeContextMenus();
    }

    initializeFileListState() {
        const fileList = document.getElementById('fileList');
        const minimizedButton = document.querySelector('.minimized-filelist-button');
        const isCollapsed = localStorage.getItem('fileListCollapsed') === 'true';

        if (isCollapsed && fileList && minimizedButton) {
            fileList.classList.add('collapsed');
            minimizedButton.style.display = 'block';
        } else if (minimizedButton) {
            minimizedButton.style.display = 'none';
        }

        if (fileListResizer && typeof fileListResizer.updateContainerWidth === 'function') {
            fileListResizer.updateContainerWidth();
        }
    }

    toggleFileList() {
        const fileList = document.getElementById('fileList');
        const minimizedButton = document.querySelector('.minimized-filelist-button');
        if (!fileList || !minimizedButton) return;

        const isCollapsed = fileList.classList.toggle('collapsed');
        minimizedButton.style.display = isCollapsed ? 'block' : 'none';
        localStorage.setItem('fileListCollapsed', isCollapsed ? 'true' : 'false');

        if (fileListResizer && typeof fileListResizer.updateContainerWidth === 'function') {
            fileListResizer.updateContainerWidth();
        }
    }

    initializeContextMenus() {
        // 关闭所有右键菜单
        document.addEventListener('click', () => {
            document.querySelectorAll('.context-menu').forEach(m => m.style.display = 'none');
        });

        // 文件列表空白区域右键
        if (this.fileListItems) {
            this.fileListItems.addEventListener('contextmenu', (e) => {
                // 只在空白区域触发
                if (e.target === this.fileListItems) {
                    e.preventDefault();
                    this.showContextMenu('rootContextMenu', e.clientX, e.clientY);
                }
            });
        }
    }

    showContextMenu(menuId, x, y, data) {
        document.querySelectorAll('.context-menu').forEach(m => m.style.display = 'none');
        const menu = document.getElementById(menuId);
        if (!menu) return;

        if (data) {
            Object.entries(data).forEach(([key, value]) => {
                menu.setAttribute(`data-${key}`, value);
            });
        }

        menu.style.display = 'block';
        menu.style.left = `${x}px`;
        menu.style.top = `${y}px`;

        // 确保菜单不超出视口
        const rect = menu.getBoundingClientRect();
        if (rect.right > window.innerWidth) {
            menu.style.left = `${window.innerWidth - rect.width - 5}px`;
        }
        if (rect.bottom > window.innerHeight) {
            menu.style.top = `${window.innerHeight - rect.height - 5}px`;
        }
    }

    // ==================== API 调用 ====================

    async apiGet(endpoint, params) {
        const url = new URL(API_BASE + endpoint, window.location.origin);
        if (params) {
            Object.entries(params).forEach(([k, v]) => {
                if (v !== undefined && v !== null) url.searchParams.set(k, v);
            });
        }
        const resp = await fetch(url);
        if (!resp.ok) {
            const err = await resp.json().catch(() => ({ message: resp.statusText }));
            throw new Error(err.message || '请求失败');
        }
        return resp.json();
    }

    async apiPost(endpoint, body) {
        const resp = await fetch(API_BASE + endpoint, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (!resp.ok) {
            const err = await resp.json().catch(() => ({ message: resp.statusText }));
            throw new Error(err.message || '请求失败');
        }
        return resp.json();
    }

    async apiPut(endpoint, body) {
        const resp = await fetch(API_BASE + endpoint, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (!resp.ok) {
            const err = await resp.json().catch(() => ({ message: resp.statusText }));
            throw new Error(err.message || '请求失败');
        }
        return resp.json();
    }

    async apiDelete(endpoint, params) {
        const url = new URL(API_BASE + endpoint, window.location.origin);
        if (params) {
            Object.entries(params).forEach(([k, v]) => {
                if (v !== undefined && v !== null) url.searchParams.set(k, v);
            });
        }
        const resp = await fetch(url, { method: 'DELETE' });
        if (!resp.ok) {
            const err = await resp.json().catch(() => ({ message: resp.statusText }));
            throw new Error(err.message || '请求失败');
        }
        return resp.json();
    }

    // ==================== 文件树操作 ====================

    async loadFileList() {
        try {
            const wsInfo = await this.apiGet('/workspace');
            this.workspaceRoot = wsInfo.path;

            // 更新工作区标题
            this.updateWorkspaceTitle();

            const tree = await this.apiGet('/tree');
            this.treeData = tree;
            this.displayFileList(tree);
        } catch (error) {
            console.error('加载文件列表失败:', error);
            this.fileListItems.innerHTML = '<li class="empty-hint">无法加载文件列表</li>';
        }
    }

    updateWorkspaceTitle() {
        const titleEl = document.getElementById('workspaceTitle');
        if (titleEl && this.workspaceRoot) {
            const folderName = this.workspaceRoot.split(/[/\\]/).pop() || this.workspaceRoot;
            titleEl.textContent = folderName;
            titleEl.title = this.workspaceRoot;
        }
    }

    displayFileList(tree) {
        if (!this.fileListItems) return;
        this.fileListItems.innerHTML = '';

        if (!tree || tree.length === 0) {
            this.fileListItems.innerHTML = '<li class="empty-hint">空文件夹，点击 + 创建文件</li>';
            return;
        }

        tree.forEach(node => {
            const el = this.createTreeNode(node);
            this.fileListItems.appendChild(el);
        });
    }

    createTreeNode(node) {
        const li = document.createElement('li');

        if (node.type === 'folder') {
            li.className = 'folder';
            const isExpanded = this.expandedFolders.has(node.path);

            // 文件夹头部
            const header = document.createElement('div');
            header.className = 'folder-header' + (isExpanded ? ' open' : '');
            header.setAttribute('data-path', node.path);

            // 展开/折叠箭头
            const arrow = document.createElement('span');
            arrow.className = 'tree-arrow' + (isExpanded ? ' open' : '');
            arrow.textContent = '▶';
            header.appendChild(arrow);

            const nameSpan = document.createElement('span');
            nameSpan.className = 'folder-name';
            nameSpan.textContent = node.name;
            header.appendChild(nameSpan);

            header.addEventListener('click', (e) => {
                e.stopPropagation();
                this.toggleFolder(node.path, li, header);
            });

            header.addEventListener('contextmenu', (e) => {
                e.preventDefault();
                e.stopPropagation();
                this.showContextMenu('folderContextMenu', e.clientX, e.clientY, { path: node.path });
            });

            li.appendChild(header);

            // 文件夹内容
            const content = document.createElement('ul');
            content.className = 'folder-content' + (isExpanded ? ' open' : '');

            if (node.children && node.children.length > 0) {
                node.children.forEach(child => {
                    content.appendChild(this.createTreeNode(child));
                });
            }

            li.appendChild(content);
        } else {
            // 文件
            const a = document.createElement('a');
            a.setAttribute('data-path', node.path);
            a.textContent = node.name;
            a.title = node.path;

            if (this.currentFilePath === node.path) {
                a.classList.add('selected');
            }

            a.addEventListener('click', (e) => {
                e.preventDefault();
                e.stopPropagation();
                this.openFile(node.path);
            });

            a.addEventListener('contextmenu', (e) => {
                e.preventDefault();
                e.stopPropagation();
                this.showContextMenu('fileContextMenu', e.clientX, e.clientY, { path: node.path });
            });

            li.appendChild(a);
        }

        return li;
    }

    toggleFolder(folderPath, li, header) {
        const content = li.querySelector('.folder-content');
        const arrow = header.querySelector('.tree-arrow');
        if (!content) return;

        const isOpen = content.classList.toggle('open');
        header.classList.toggle('open', isOpen);
        if (arrow) arrow.classList.toggle('open', isOpen);

        if (isOpen) {
            this.expandedFolders.add(folderPath);
        } else {
            this.expandedFolders.delete(folderPath);
        }
        this.saveExpandedFolders();
    }

    saveExpandedFolders() {
        localStorage.setItem('expandedFolders', JSON.stringify([...this.expandedFolders]));
    }

    // ==================== 文件操作 ====================

    async openFile(filePath) {
        try {
            // 先保存当前文件
            await this.saveCurrentFile();

            const data = await this.apiGet('/file', { path: filePath });
            this.currentFilePath = filePath;

            // 更新编辑器内容
            if (window.editor) {
                window.__suppressAutoSave = true;
                window.editor.setValue(data.content || '');
                window.__suppressAutoSave = false;
            }

            // 更新选中状态
            this.updateSelection(filePath);

            // 读取文件关联的项目类型（从 .sharppad 元数据文件）
            this.loadFileMetadata(filePath);
        } catch (error) {
            showNotification('打开文件失败: ' + error.message, 'error');
        }
    }

    updateSelection(filePath) {
        document.querySelectorAll('#fileListItems a.selected').forEach(a => a.classList.remove('selected'));
        const target = document.querySelector(`#fileListItems a[data-path="${CSS.escape(filePath)}"]`);
        if (target) {
            target.classList.add('selected');
        }
    }

    async saveCurrentFile() {
        if (!this.currentFilePath || !window.editor) return;

        try {
            const content = window.editor.getValue();
            await this.apiPut('/file', {
                path: this.currentFilePath,
                content: content
            });
        } catch (error) {
            console.error('保存文件失败:', error);
        }
    }

    async saveFile(filePath, content) {
        try {
            await this.apiPut('/file', { path: filePath, content: content || '' });
        } catch (error) {
            showNotification('保存失败: ' + error.message, 'error');
        }
    }

    // 兼容旧的 saveFileToLocalStorage 接口，现在保存到服务器
    async saveFileToLocalStorage(fileId, code, options) {
        // fileId 在新系统中即为 filePath
        if (!this.currentFilePath) return;
        try {
            await this.apiPut('/file', { path: this.currentFilePath, content: code || '' });
        } catch (error) {
            if (!options?.silent) {
                console.error('保存文件失败:', error);
            }
        }
    }

    async addFile(parentPath) {
        const name = await customPrompt('新建文件', '请输入文件名:', 'Program.cs');
        if (!name) return;

        const filePath = parentPath ? `${parentPath}/${name}` : name;
        try {
            await this.apiPost('/file', { path: filePath, content: DEFAULT_CODE });
            await this.loadFileList();
            // 自动打开新文件
            await this.openFile(filePath);
            showNotification('文件已创建', 'success');
        } catch (error) {
            showNotification('创建文件失败: ' + error.message, 'error');
        }
    }

    async addFolder(parentPath) {
        const name = await customPrompt('新建文件夹', '请输入文件夹名:');
        if (!name) return;

        const folderPath = parentPath ? `${parentPath}/${name}` : name;
        try {
            await this.apiPost('/folder', { path: folderPath });
            // 展开父文件夹
            if (parentPath) {
                this.expandedFolders.add(parentPath);
                this.saveExpandedFolders();
            }
            await this.loadFileList();
            showNotification('文件夹已创建', 'success');
        } catch (error) {
            showNotification('创建文件夹失败: ' + error.message, 'error');
        }
    }

    async addFileToFolder(folderPath) {
        if (!folderPath) return;
        this.expandedFolders.add(folderPath);
        this.saveExpandedFolders();
        await this.addFile(folderPath);
    }

    async addFolderToFolder(parentFolderPath) {
        if (!parentFolderPath) return;
        this.expandedFolders.add(parentFolderPath);
        this.saveExpandedFolders();
        await this.addFolder(parentFolderPath);
    }

    async renameFile() {
        const menu = document.getElementById('fileContextMenu');
        const filePath = menu?.getAttribute('data-path');
        if (!filePath) return;

        const oldName = filePath.split('/').pop();
        const newName = await customPrompt('重命名文件', '请输入新文件名:', oldName);
        if (!newName || newName === oldName) return;

        const parentPath = filePath.includes('/') ? filePath.substring(0, filePath.lastIndexOf('/')) : '';
        const newPath = parentPath ? `${parentPath}/${newName}` : newName;

        try {
            await this.apiPost('/rename', { oldPath: filePath, newPath: newPath });

            // 如果重命名的是当前打开的文件，更新路径
            if (this.currentFilePath === filePath) {
                this.currentFilePath = newPath;
            }

            await this.loadFileList();
            showNotification('重命名成功', 'success');
        } catch (error) {
            showNotification('重命名失败: ' + error.message, 'error');
        }
    }

    async renameFolder() {
        const menu = document.getElementById('folderContextMenu');
        const folderPath = menu?.getAttribute('data-path');
        if (!folderPath) return;

        const oldName = folderPath.split('/').pop();
        const newName = await customPrompt('重命名文件夹', '请输入新名称:', oldName);
        if (!newName || newName === oldName) return;

        const parentPath = folderPath.includes('/') ? folderPath.substring(0, folderPath.lastIndexOf('/')) : '';
        const newPath = parentPath ? `${parentPath}/${newName}` : newName;

        try {
            await this.apiPost('/rename', { oldPath: folderPath, newPath: newPath });

            // 更新展开状态
            if (this.expandedFolders.has(folderPath)) {
                this.expandedFolders.delete(folderPath);
                this.expandedFolders.add(newPath);
                this.saveExpandedFolders();
            }

            // 如果当前文件在被重命名的文件夹下，更新路径
            if (this.currentFilePath && this.currentFilePath.startsWith(folderPath + '/')) {
                this.currentFilePath = newPath + this.currentFilePath.substring(folderPath.length);
            }

            await this.loadFileList();
            showNotification('重命名成功', 'success');
        } catch (error) {
            showNotification('重命名失败: ' + error.message, 'error');
        }
    }

    async deleteFile() {
        const menu = document.getElementById('fileContextMenu');
        const filePath = menu?.getAttribute('data-path');
        if (!filePath) return;

        const fileName = filePath.split('/').pop();
        const confirmed = await customConfirm('删除文件', `确定要删除 "${fileName}" 吗？`);
        if (!confirmed) return;

        try {
            await this.apiDelete('/item', { path: filePath });

            if (this.currentFilePath === filePath) {
                this.currentFilePath = null;
                if (window.editor) {
                    window.__suppressAutoSave = true;
                    window.editor.setValue('');
                    window.__suppressAutoSave = false;
                }
            }

            await this.loadFileList();
            showNotification('文件已删除', 'success');
        } catch (error) {
            showNotification('删除失败: ' + error.message, 'error');
        }
    }

    async deleteFolder() {
        const menu = document.getElementById('folderContextMenu');
        const folderPath = menu?.getAttribute('data-path');
        if (!folderPath) return;

        const folderName = folderPath.split('/').pop();
        const confirmed = await customConfirm('删除文件夹', `确定要删除 "${folderName}" 及其所有内容吗？`);
        if (!confirmed) return;

        try {
            await this.apiDelete('/item', { path: folderPath });

            this.expandedFolders.delete(folderPath);
            this.saveExpandedFolders();

            if (this.currentFilePath && this.currentFilePath.startsWith(folderPath + '/')) {
                this.currentFilePath = null;
                if (window.editor) {
                    window.__suppressAutoSave = true;
                    window.editor.setValue('');
                    window.__suppressAutoSave = false;
                }
            }

            await this.loadFileList();
            showNotification('文件夹已删除', 'success');
        } catch (error) {
            showNotification('删除失败: ' + error.message, 'error');
        }
    }

    async openFolder() {
        const path = await customPrompt('打开文件夹', '请输入文件夹路径:');
        if (!path) return;

        try {
            await this.apiPost('/workspace', { path: path });
            this.currentFilePath = null;
            this.expandedFolders.clear();
            this.saveExpandedFolders();
            if (window.editor) {
                window.__suppressAutoSave = true;
                window.editor.setValue('');
                window.__suppressAutoSave = false;
            }
            await this.loadFileList();
            showNotification('已打开文件夹', 'success');
        } catch (error) {
            showNotification('打开文件夹失败: ' + error.message, 'error');
        }
    }

    // ==================== NuGet 配置（元数据文件） ====================

    getMetadataPath(filePath) {
        const dir = filePath.includes('/') ? filePath.substring(0, filePath.lastIndexOf('/')) : '';
        const name = filePath.split('/').pop();
        return dir ? `${dir}/.sharppad/${name}.json` : `.sharppad/${name}.json`;
    }

    async loadFileMetadata(filePath) {
        try {
            const metaPath = this.getMetadataPath(filePath);
            const data = await this.apiGet('/file', { path: metaPath });
            const meta = JSON.parse(data.content || '{}');

            // 恢复项目类型
            if (meta.projectType) {
                const select = document.getElementById('projectTypeSelect');
                if (select) {
                    select.value = meta.projectType;
                    window.dispatchEvent(new CustomEvent(PROJECT_TYPE_CHANGE_EVENT, {
                        detail: { projectType: meta.projectType }
                    }));
                }
            }

            // 恢复 NuGet 包
            if (meta.nugetPackages && meta.nugetPackages.length > 0) {
                this.checkAndRestoreFilePackages(meta.nugetPackages);
            }
        } catch {
            // 元数据文件不存在是正常的
        }
    }

    async saveFileMetadata(filePath, metadata) {
        try {
            const metaPath = this.getMetadataPath(filePath);
            await this.apiPut('/file', {
                path: metaPath,
                content: JSON.stringify(metadata, null, 2)
            });
        } catch (error) {
            console.error('保存元数据失败:', error);
        }
    }

    async configureNuGet() {
        const menu = document.getElementById('fileContextMenu');
        const filePath = menu?.getAttribute('data-path') || this.currentFilePath;
        if (!filePath) {
            showNotification('请先选择一个文件', 'error');
            return;
        }

        // 构造兼容旧 NuGet 管理器的 file 对象
        let meta = {};
        try {
            const metaPath = this.getMetadataPath(filePath);
            const data = await this.apiGet('/file', { path: metaPath });
            meta = JSON.parse(data.content || '{}');
        } catch {
            // 元数据不存在
        }

        const file = {
            id: filePath,
            name: filePath.split('/').pop(),
            nugetConfig: { packages: meta.nugetPackages || [] }
        };

        if (typeof window.loadNuGetConfig === 'function') {
            window.loadNuGetConfig(file);
        }
    }

    async checkAndRestoreFilePackages(packages) {
        if (!packages || packages.length === 0) return;

        try {
            const { sendRequest } = await import('../utils/apiService.js');
            await sendRequest('addPackages', {
                Packages: packages.map(p => ({
                    Id: p.id || p.Id,
                    Version: p.version || p.Version || ''
                }))
            });
        } catch (error) {
            console.warn('恢复 NuGet 包失败:', error);
        }
    }

    // ==================== 搜索过滤 ====================

    filterFiles(keyword) {
        if (!this.fileListItems) return;
        const items = this.fileListItems.querySelectorAll('li');
        const lower = (keyword || '').toLowerCase();

        items.forEach(li => {
            if (!lower) {
                li.style.display = '';
                return;
            }

            const a = li.querySelector('a');
            const folderName = li.querySelector('.folder-name');
            const name = a?.textContent || folderName?.textContent || '';

            if (li.classList.contains('folder')) {
                // 搜索文件夹内的文件
                const hasMatch = Array.from(li.querySelectorAll('a')).some(
                    link => link.textContent.toLowerCase().includes(lower)
                );
                li.style.display = (name.toLowerCase().includes(lower) || hasMatch) ? '' : 'none';

                // 自动展开包含匹配结果的文件夹
                if (hasMatch) {
                    const content = li.querySelector('.folder-content');
                    const header = li.querySelector('.folder-header');
                    const arrow = li.querySelector('.tree-arrow');
                    if (content) content.classList.add('open');
                    if (header) header.classList.add('open');
                    if (arrow) arrow.classList.add('open');
                }
            } else {
                li.style.display = name.toLowerCase().includes(lower) ? '' : 'none';
            }
        });
    }

    // ==================== 兼容性方法 ====================

    // 获取当前文件信息（兼容旧接口）
    getCurrentFileInfo() {
        if (!this.currentFilePath) return null;
        return {
            id: this.currentFilePath,
            name: this.currentFilePath.split('/').pop(),
            path: this.currentFilePath,
            type: 'file'
        };
    }

    // 保存代码（兼容 auto-save）
    saveCode(code) {
        if (!this.currentFilePath) return;
        this.saveFile(this.currentFilePath, code);
    }

    // 获取当前文件路径
    getCurrentFilePath() {
        return this.currentFilePath;
    }
}

export { FileManager };
