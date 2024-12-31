// 文件系统管理模块
class FileManager {
    constructor() {
        this.fileListItems = document.getElementById('fileListItems');
        this.initializeEventListeners();
    }

    initializeEventListeners() {
        // 文件过滤器监听
        const fileFilter = document.getElementById('fileFilter');
        if (fileFilter) {
            fileFilter.addEventListener('keyup', (e) => this.filterFiles(e.target.value));
        }

        // 添加文件按钮监听
        const addFileBtn = document.getElementById('addFileBtn');
        if (addFileBtn) {
            addFileBtn.addEventListener('click', () => this.addFile());
        }

        // 添加文件夹按钮监听
        const addFolderBtn = document.getElementById('addFolderBtn');
        if (addFolderBtn) {
            addFolderBtn.addEventListener('click', () => this.addFolder());
        }

        // 初始化右键菜单事件
        this.initializeContextMenus();
    }

    initializeContextMenus() {
        // 文件右键菜单
        const fileContextMenu = document.getElementById('fileContextMenu');
        if (fileContextMenu) {
            // 重命名文件
            fileContextMenu.querySelector('.rename')?.addEventListener('click', () => {
                const fileId = fileContextMenu.getAttribute('data-target-file-id');
                const files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');
                const file = this.findFileById(files, fileId);
                if (file) {
                    this.renameFile(file);
                }
                fileContextMenu.style.display = 'none';
            });

            // 删除文件
            fileContextMenu.querySelector('.delete')?.addEventListener('click', () => {
                const fileId = fileContextMenu.getAttribute('data-target-file-id');
                const files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');
                const file = this.findFileById(files, fileId);
                if (file) {
                    this.deleteFile(file);
                }
                fileContextMenu.style.display = 'none';
            });
        }

        // 文件夹右键菜单
        const folderContextMenu = document.getElementById('folderContextMenu');
        if (folderContextMenu) {
            // 重命名文件夹
            folderContextMenu.querySelector('.rename')?.addEventListener('click', () => {
                const folderId = folderContextMenu.getAttribute('data-folder-id');
                const files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');
                const folder = this.findFileById(files, folderId);
                if (folder) {
                    this.renameFolder(folder);
                }
                folderContextMenu.style.display = 'none';
            });

            // 删除文件夹
            folderContextMenu.querySelector('.delete')?.addEventListener('click', () => {
                const folderId = folderContextMenu.getAttribute('data-folder-id');
                const files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');
                const folder = this.findFileById(files, folderId);
                if (folder) {
                    this.deleteFolder(folder);
                }
                folderContextMenu.style.display = 'none';
            });
        }

        // 点击其他地方关闭菜单
        document.addEventListener('click', (e) => {
            if (!e.target.closest('.context-menu')) {
                const menus = document.querySelectorAll('.context-menu');
                menus.forEach(menu => menu.style.display = 'none');
            }
        });
    }

    // 保存展开的文件夹状态
    saveExpandedFolders() {
        const expandedFolders = [];
        document.querySelectorAll('.folder-content.open').forEach(content => {
            const folderId = content.getAttribute('data-folder-content');
            if (folderId) {
                expandedFolders.push(folderId);
            }
        });
        return expandedFolders;
    }

    // 恢复文件夹展开状态
    restoreExpandedFolders(expandedFolders) {
        expandedFolders.forEach(folderId => {
            const content = document.querySelector(`[data-folder-content="${folderId}"]`);
            const header = document.querySelector(`[data-folder-header="${folderId}"]`);
            if (content && header) {
                content.classList.add('open');
                header.classList.add('open');
            }
        });
    }

    loadFileList() {
        const expandedFolders = this.saveExpandedFolders(); // 保存当前展开状态
        const filesData = localStorage.getItem('controllerFiles');
        if (filesData) {
            try {
                const files = JSON.parse(filesData);
                this.displayFileList(files);
                this.restoreExpandedFolders(expandedFolders); // 恢复展开状态

                // 加载当前文件
                const currentFileId = localStorage.getItem('currentFileId');
                if (currentFileId) {
                    const currentFile = this.findFileById(files, currentFileId);
                    if (currentFile) {
                        this.openFile(currentFile);
                    }
                }
            } catch (error) {
                console.error('Error parsing files from localStorage:', error);
            }
        } else {
            this.displayFileList([]);
        }
    }

    displayFileList(files) {
        if (!this.fileListItems) return;
        
        this.fileListItems.innerHTML = '';
        files.forEach(file => {
            const element = this.createFileElement(file);
            this.fileListItems.appendChild(element);
        });

        // 高亮当前文件
        const currentFileId = localStorage.getItem('currentFileId');
        if (currentFileId) {
            const currentLink = this.fileListItems.querySelector(`a[data-id="${currentFileId}"]`);
            if (currentLink) {
                currentLink.classList.add('selected');
            }
        }
    }

    createFileElement(file) {
        const li = document.createElement('li');

        if (file.type === 'folder') {
            // 创建文件夹结构
            const folderDiv = document.createElement('div');
            folderDiv.className = 'folder';

            const folderHeader = document.createElement('div');
            folderHeader.className = 'folder-header';
            folderHeader.textContent = file.name;
            folderHeader.setAttribute('data-folder-header', file.id);

            // 添加拖拽功能
            folderHeader.draggable = true;
            folderHeader.addEventListener('dragstart', (e) => {
                e.dataTransfer.setData('text/plain', file.id);
                e.dataTransfer.effectAllowed = 'move';
            });

            // 添加拖放目标功能
            folderHeader.addEventListener('dragover', (e) => {
                e.preventDefault();
                folderHeader.classList.add('drag-over');
            });

            folderHeader.addEventListener('dragleave', () => {
                folderHeader.classList.remove('drag-over');
            });

            folderHeader.addEventListener('drop', (e) => {
                e.preventDefault();
                folderHeader.classList.remove('drag-over');
                const draggedId = e.dataTransfer.getData('text/plain');
                this.moveFileToFolder(draggedId, file.id);
            });

            // 添加点击展开/折叠功能
            folderHeader.addEventListener('click', (e) => {
                if (e.target === folderHeader) {
                    this.toggleFolder(file.id);
                }
            });

            // 添加右键菜单
            folderHeader.addEventListener('contextmenu', (e) => {
                e.preventDefault();
                this.showFolderContextMenu(e, file);
            });

            const folderContent = document.createElement('div');
            folderContent.className = 'folder-content';
            folderContent.setAttribute('data-folder-content', file.id);

            // 递归创建子文件和文件夹
            if (file.files) {
                file.files.forEach(childFile => {
                    const childElement = this.createFileElement(childFile);
                    folderContent.appendChild(childElement);
                });
            }

            folderDiv.appendChild(folderHeader);
            folderDiv.appendChild(folderContent);
            li.appendChild(folderDiv);
        } else {
            // 创建文件链接
            const fileContainer = document.createElement('div');
            fileContainer.className = 'file-container';

            const a = document.createElement('a');
            a.href = '#';
            a.className = 'file-name';
            a.textContent = file.name;
            a.setAttribute('data-id', file.id);

            // 添加拖拽功能
            a.draggable = true;
            a.addEventListener('dragstart', (e) => {
                e.dataTransfer.setData('text/plain', file.id);
                e.dataTransfer.effectAllowed = 'move';
            });

            // 添加文件相关的事件监听器
            a.addEventListener('click', (e) => {
                e.preventDefault();
                this.openFile(file);
            });

            a.addEventListener('contextmenu', (e) => {
                e.preventDefault();
                this.showContextMenu(e, file);
            });

            fileContainer.appendChild(a);
            li.appendChild(fileContainer);
        }

        return li;
    }

    toggleFolder(folderId) {
        const content = document.querySelector(`[data-folder-content="${folderId}"]`);
        const header = document.querySelector(`[data-folder-header="${folderId}"]`);

        if (!header.hasAttribute('draggable')) {
            header.draggable = true;
            header.addEventListener('dragstart', (e) => {
                e.dataTransfer.setData('text/plain', folderId);
                e.dataTransfer.effectAllowed = 'move';
            });
        }

        content.classList.toggle('open');
        header.classList.toggle('open');
    }

    moveFileToFolder(fileId, folderId) {
        const files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');
        const file = this.findFileById(files, fileId);
        const folder = this.findFileById(files, folderId);

        if (file && folder && folder.type === 'folder') {
            // 从原位置移除文件
            this.removeFileById(files, fileId);
            // 添加到新文件夹
            folder.files = folder.files || [];
            folder.files.push(file);
            localStorage.setItem('controllerFiles', JSON.stringify(files));
            this.loadFileList();
        }
    }

    findFileById(files, id) {
        for (const file of files) {
            if (file.id === id) return file;
            if (file.type === 'folder' && file.files) {
                const found = this.findFileById(file.files, id);
                if (found) return found;
            }
        }
        return null;
    }

    removeFileById(files, id) {
        for (let i = 0; i < files.length; i++) {
            if (files[i].id === id) {
                files.splice(i, 1);
                return true;
            }
            if (files[i].type === 'folder' && files[i].files) {
                if (this.removeFileById(files[i].files, id)) return true;
            }
        }
        return false;
    }

    addFolder() {
        const folderName = prompt('请输入文件夹名称：', 'New Folder');
        if (!folderName) return;

        const files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');
        const newFolder = {
            id: Date.now().toString(),
            name: folderName,
            type: 'folder',
            files: []
        };

        files.push(newFolder);
        localStorage.setItem('controllerFiles', JSON.stringify(files));
        this.loadFileList();
    }

    showFolderContextMenu(e, folder) {
        e.preventDefault();
        const menu = document.getElementById('folderContextMenu');
        menu.style.display = 'block';
        menu.style.left = e.pageX + 'px';
        menu.style.top = e.pageY + 'px';
        menu.setAttribute('data-folder-id', folder.id);
    }

    renameFolder(folder) {
        const newName = prompt('请输入新的文件夹名:', folder.name);
        if (newName && newName !== folder.name) {
            const files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');
            const targetFolder = this.findFileById(files, folder.id);
            if (targetFolder) {
                targetFolder.name = newName;
                localStorage.setItem('controllerFiles', JSON.stringify(files));
                this.loadFileList();
            }
        }
    }

    deleteFolder(folder) {
        if (confirm(`确定要删除文件夹 "${folder.name}" 及其所有内容吗？`)) {
            const files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');
            this.removeFileById(files, folder.id);
            localStorage.setItem('controllerFiles', JSON.stringify(files));
            this.loadFileList();
        }
    }

    openFile(file) {
        // 移除之前的选中状态
        const selectedFile = this.fileListItems.querySelector('.selected');
        if (selectedFile) {
            selectedFile.classList.remove('selected');
        }

        // 设置新的选中状态
        const fileLink = this.fileListItems.querySelector(`a[data-id="${file.id}"]`);
        if (fileLink) {
            fileLink.classList.add('selected');
        }

        // 更新编辑器内容
        if (window.editor) {
            window.editor.setValue(file.content || '');
        }

        // 保存当前文件ID
        localStorage.setItem('currentFileId', file.id);
    }

    showContextMenu(e, file) {
        e.preventDefault();
        const menu = document.getElementById('fileContextMenu');
        menu.style.display = 'block';
        menu.style.left = e.pageX + 'px';
        menu.style.top = e.pageY + 'px';
        menu.setAttribute('data-target-file-id', file.id);
    }

    addFile() {
        const files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');
        const newFile = {
            id: Date.now().toString(),
            name: 'New File.cs',
            content: `using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;

class Program
{
    public static async Task Main()
    {
         Console.WriteLine("Hello, World!");
    }
}`,
            nugetConfig: {
                packages: []
            }
        };
        files.push(newFile);
        localStorage.setItem('controllerFiles', JSON.stringify(files));
        this.displayFileList(files);
        this.openFile(newFile);
    }

    renameFile(file) {
        const newName = prompt('请输入新的文件名:', file.name);
        if (newName && newName !== file.name) {
            const files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');
            const targetFile = this.findFileById(files, file.id);
            if (targetFile) {
                targetFile.name = newName;
                localStorage.setItem('controllerFiles', JSON.stringify(files));
                this.loadFileList();
            }
        }
    }

    deleteFile(file) {
        if (confirm(`确定要删除文件 "${file.name}" 吗？`)) {
            const files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');
            this.removeFileById(files, file.id);
            localStorage.setItem('controllerFiles', JSON.stringify(files));
            this.loadFileList();

            // 如果删除的是当前文件，清空编辑器
            const currentFileId = localStorage.getItem('currentFileId');
            if (currentFileId === file.id) {
                localStorage.removeItem('currentFileId');
                if (window.editor) {
                    window.editor.setValue('');
                }
            }
        }
    }

    filterFiles(filter) {
        // 滤文件列
        document.querySelectorAll('#fileListItems li').forEach(li => {
            if (li.textContent.includes(filter)) {
                li.style.display = 'block';
            } else {
                li.style.display = 'none';
            }
        });

        console.log(filter);
    }
}

export { FileManager }; 