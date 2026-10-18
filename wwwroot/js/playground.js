let sessionId = localStorage.getItem("rag_playground_session") || generateSessionId();
let currentDocName = "Microsoft-Copilot-Studio-Licensing-Guide.pdf";
let isProcessing = false;

const SVG_ICONS = {
    bot: `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2a3 3 0 0 0-3 3v1H6a2 2 0 0 0-2 2v10a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-3V5a3 3 0 0 0-3-3Z"/><path d="M9 13v.01"/><path d="M15 13v.01"/><path d="M10 17h4"/></svg>`,
    user: `<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>`,
    copy: `<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect width="14" height="14" x="8" y="8" rx="2" ry="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/></svg>`,
    check: `<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>`,
    chevron: `<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"/></svg>`,
    file: `<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14.5 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7.5L14.5 2z"/><polyline points="14 2 14 8 20 8"/></svg>`
};

document.addEventListener("DOMContentLoaded", () => {
    initEventListeners();
    initDropzone();
});

function generateSessionId() {
    const id = "sess_" + Math.random().toString(36).substring(2, 11);
    localStorage.setItem("rag_playground_session", id);
    return id;
}

function initEventListeners() {
    const sendBtn = document.getElementById("sendBtn");
    const chatInput = document.getElementById("chatInput");
    const clearChatBtn = document.getElementById("clearChatBtn");
    const uploadNavBtn = document.getElementById("uploadNavBtn");
    const dockAttachBtn = document.getElementById("dockAttachBtn");
    const pdfFileInput = document.getElementById("pdfFileInput");

    sendBtn.addEventListener("click", handleSendMessage);
    chatInput.addEventListener("keydown", (e) => {
        if (e.key === "Enter" && !e.shiftKey) {
            e.preventDefault();
            handleSendMessage();
        }
    });

    chatInput.addEventListener("input", () => {
        chatInput.style.height = "auto";
        chatInput.style.height = Math.min(chatInput.scrollHeight, 140) + "px";
    });

    clearChatBtn.addEventListener("click", async () => {
        try {
            await fetch(`/api/history/${encodeURIComponent(sessionId)}`, { method: "DELETE" });
        } catch (e) {
            console.warn("Clear history error:", e);
        }
        clearChatUI();
    });

    const triggerFilePicker = () => pdfFileInput.click();
    uploadNavBtn.addEventListener("click", triggerFilePicker);
    dockAttachBtn.addEventListener("click", triggerFilePicker);

    pdfFileInput.addEventListener("change", (e) => {
        if (e.target.files && e.target.files[0]) {
            uploadAndIngestPdf(e.target.files[0]);
            pdfFileInput.value = "";
        }
    });
}

function initDropzone() {
    const dropzone = document.getElementById("quickDropzone");
    const pdfFileInput = document.getElementById("pdfFileInput");
    if (!dropzone) return;

    dropzone.addEventListener("click", () => pdfFileInput.click());

    ["dragenter", "dragover"].forEach((evt) => {
        dropzone.addEventListener(evt, (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropzone.classList.add("drag-over");
        });
    });

    ["dragleave", "drop"].forEach((evt) => {
        dropzone.addEventListener(evt, (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropzone.classList.remove("drag-over");
        });
    });

    dropzone.addEventListener("drop", (e) => {
        const dt = e.dataTransfer;
        if (dt && dt.files && dt.files[0]) {
            uploadAndIngestPdf(dt.files[0]);
        }
    });
}

async function uploadAndIngestPdf(file) {
    const toast = document.getElementById("uploadToast");
    const toastTitle = document.getElementById("toastTitle");
    const toastSubtitle = document.getElementById("toastSubtitle");

    toastTitle.textContent = `Ingesting "${file.name}"...`;
    toastSubtitle.textContent = `Extracting pages, generating vector embeddings, and storing in Qdrant...`;
    toast.style.display = "flex";

    const formData = new FormData();
    formData.append("file", file);

    try {
        const resp = await fetch("/api/upload", {
            method: "POST",
            body: formData
        });
        const data = await resp.json();

        if (resp.ok && data.success && data.fileName) {
            toastTitle.textContent = "Ingestion Complete!";
            toastSubtitle.textContent = `Indexed ${data.pageCount || "all"} pages (${data.chunkCount || ""} chunks) successfully.`;
            currentDocName = data.fileName;

            const tag = document.getElementById("headerActiveDocTag");
            if (tag) {
                tag.textContent = data.fileName.length > 28 ? data.fileName.substring(0, 25) + "..." : data.fileName;
                tag.title = data.fileName;
            }

            setTimeout(() => {
                toast.style.display = "none";
            }, 1400);
        } else {
            toastTitle.textContent = "Upload Failed";
            toastSubtitle.textContent = data.error || "An error occurred during ingestion.";
            setTimeout(() => { toast.style.display = "none"; }, 3500);
        }
    } catch (err) {
        toastTitle.textContent = "Network Error";
        toastSubtitle.textContent = err.message;
        setTimeout(() => { toast.style.display = "none"; }, 3500);
    }
}

async function handleSendMessage() {
    const chatInput = document.getElementById("chatInput");
    const sendBtn = document.getElementById("sendBtn");
    const question = chatInput.value.trim();

    if (!question || isProcessing) return;

    isProcessing = true;
    sendBtn.disabled = true;
    chatInput.value = "";
    chatInput.style.height = "auto";

    const emptyState = document.getElementById("emptyState");
    if (emptyState) emptyState.style.display = "none";

    renderMessage("user", question);

    const typingIndicator = renderTypingIndicator();

    const payload = {
        sessionId: sessionId,
        userId: "default-user",
        question: question,
        topK: 4,
        minRelevanceScore: 0.05,
        selectedDocumentName: currentDocName
    };

    try {
        const response = await fetch("/api/chat", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
        });

        if (typingIndicator && typingIndicator.parentNode) {
            typingIndicator.parentNode.removeChild(typingIndicator);
        }

        const data = await response.json();

        if (response.ok && data.isSuccess) {
            renderMessage(
                "assistant", 
                data.answer, 
                data.citations
            );
        } else {
            renderMessage("assistant", data.errorMessage || data.error || "An unexpected error occurred while processing your query.");
        }
    } catch (err) {
        if (typingIndicator && typingIndicator.parentNode) {
            typingIndicator.parentNode.removeChild(typingIndicator);
        }
        renderMessage("assistant", "Network error communicating with the chat endpoint: " + err.message);
    } finally {
        isProcessing = false;
        sendBtn.disabled = false;
        chatInput.focus();
    }
}

function renderMessage(role, content, citations = null) {
    const container = document.getElementById("messagesContainer");
    const row = document.createElement("div");
    row.className = `message-row ${role}`;

    const avatar = document.createElement("div");
    avatar.className = `message-avatar ${role}`;
    avatar.innerHTML = role === "assistant" ? SVG_ICONS.bot : SVG_ICONS.user;

    const wrapper = document.createElement("div");
    wrapper.className = `message-bubble-wrapper ${role}`;

    const sender = document.createElement("div");
    sender.className = `message-sender ${role}`;
    sender.textContent = role === "assistant" ? "Copilot Assistant" : "You";

    const bubble = document.createElement("div");
    bubble.className = `message-bubble ${role}`;

    const contentDiv = document.createElement("div");
    contentDiv.className = "message-text";
    contentDiv.innerHTML = formatMarkdown(content);
    bubble.appendChild(contentDiv);

    wrapper.appendChild(sender);
    wrapper.appendChild(bubble);

    if (role === "assistant") {
        row.appendChild(avatar);
        row.appendChild(wrapper);
    } else {
        row.appendChild(wrapper);
        row.appendChild(avatar);
    }

    container.appendChild(row);
    scrollToBottom();
}

function renderTypingIndicator() {
    const container = document.getElementById("messagesContainer");
    const row = document.createElement("div");
    row.className = "message-row assistant typing-row";

    const avatar = document.createElement("div");
    avatar.className = "message-avatar assistant avatar";
    avatar.innerHTML = SVG_ICONS.bot;

    const bubble = document.createElement("div");
    bubble.className = "message-bubble assistant bubble typing-bubble";
    bubble.innerHTML = `
        <div class="typing-indicator">
            <span class="dot"></span>
            <span class="dot"></span>
            <span class="dot"></span>
        </div>
    `;

    row.appendChild(avatar);
    row.appendChild(bubble);
    container.appendChild(row);
    scrollToBottom();
    return row;
}

function clearChatUI() {
    const container = document.getElementById("messagesContainer");
    const emptyState = document.getElementById("emptyState");
    container.innerHTML = "";
    if (emptyState) {
        container.appendChild(emptyState);
        emptyState.style.display = "flex";
    }
}

function scrollToBottom() {
    const container = document.getElementById("messagesContainer");
    container.scrollTop = container.scrollHeight;
}

function formatMarkdown(text) {
    if (!text) return "";

    let escaped = escapeHtml(text);

    escaped = escaped.replace(/```([a-zA-Z0-9_-]*)\n([\s\S]*?)```/g, (match, lang, code) => {
        return `<pre><code class="lang-${lang}">${code.trim()}</code></pre>`;
    });

    escaped = escaped.replace(/`([^`]+)`/g, "<code>$1</code>");

    escaped = escaped.replace(/^### (.*$)/gim, "<h3>$1</h3>");
    escaped = escaped.replace(/^## (.*$)/gim, "<h2>$1</h2>");
    escaped = escaped.replace(/^# (.*$)/gim, "<h1>$1</h1>");

    escaped = escaped.replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>");
    escaped = escaped.replace(/\*([^*]+)\*/g, "<em>$1</em>");

    escaped = escaped.replace(/^\s*[-*]\s+(.*)$/gim, "<li>$1</li>");
    escaped = escaped.replace(/(<li>.*<\/li>)/gim, "<ul>$1</ul>");
    escaped = escaped.replace(/<\/ul>\s*<ul>/gim, "");

    escaped = escaped.replace(/^\s*(\d+)\.\s+(.*)$/gim, "<li>$2</li>");

    escaped = escaped.replace(/\n\n+/g, "</p><p>");
    escaped = `<p>${escaped}</p>`;
    escaped = escaped.replace(/<p>\s*<\/p>/g, "");
    escaped = escaped.replace(/<p>\s*(<(?:ul|ol|pre|h1|h2|h3)[^>]*>)/g, "$1");
    escaped = escaped.replace(/(<\/(?:ul|ol|pre|h1|h2|h3)>)\s*<\/p>/g, "$1");

    return escaped;
}

function escapeHtml(str) {
    if (!str) return "";
    return str
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#039;");
}