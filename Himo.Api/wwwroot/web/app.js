(() => {
  'use strict';
  const TOKEN_KEY = 'himo_web_access_token';
  const $ = (id) => document.getElementById(id);
  let token = localStorage.getItem(TOKEN_KEY) || '';
  let activeConversation = null;
  let conversations = [];
  let pollTimer = null;
  let pairing = null;
  let pollInFlight = false;
  let qrObjectUrl = null;
  let isRefreshing = false;
  const attachmentObjectUrls = new Map();

  const setPairStatus = (text, kind = '') => {
    const element = $('pairStatus');
    element.textContent = text;
    element.className = `state ${kind}`;
  };
  const randomHex = (bytes = 32) => {
    const data = crypto.getRandomValues(new Uint8Array(bytes));
    return Array.from(data, (b) => b.toString(16).padStart(2, '0')).join('');
  };
  const sha256Hex = async (value) => {
    const bytes = new TextEncoder().encode(value);
    const digest = await crypto.subtle.digest('SHA-256', bytes);
    return Array.from(new Uint8Array(digest), (b) => b.toString(16).padStart(2, '0')).join('');
  };
  const browserName = () => {
    const ua = navigator.userAgent;
    const os = /Windows/i.test(ua) ? 'Windows' : /Mac OS/i.test(ua) ? 'macOS' : /Linux/i.test(ua) ? 'Linux' : 'الكمبيوتر';
    const browser = /Edg\//i.test(ua) ? 'Edge' : /Firefox\//i.test(ua) ? 'Firefox' : /Chrome\//i.test(ua) ? 'Chrome' : 'المتصفح';
    return `${browser} على ${os}`;
  };
  async function request(path, options = {}, useToken = true) {
    const headers = new Headers(options.headers || {});
    if (useToken && token) headers.set('Authorization', `Bearer ${token}`);
    if (options.body && !(options.body instanceof FormData) && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json');
    const response = await fetch(path, { ...options, headers, cache: 'no-store' });
    if (response.status === 401 && useToken) {
      token = '';
      localStorage.removeItem(TOKEN_KEY);
      releaseAttachmentObjectUrls();
      showPairScreen();
      throw new Error('انتهت جلسة هذا المتصفح. اربطه مجددًا من الهاتف.');
    }
    if (!response.ok) {
      let message = `HTTP ${response.status}`;
      try { const data = await response.json(); message = data.message || data.detail || message; } catch { }
      throw new Error(message);
    }
    if (response.status === 204) return null;
    const type = response.headers.get('content-type') || '';
    return type.includes('application/json') ? response.json() : response;
  }

  async function renderPairingQr(payload) {
    const response = await fetch('/api/web/pair/qr', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ payload }),
      cache: 'no-store'
    });
    if (!response.ok) throw new Error(`تعذر إنشاء صورة QR (HTTP ${response.status}).`);
    const blob = await response.blob();
    if (qrObjectUrl) URL.revokeObjectURL(qrObjectUrl);
    qrObjectUrl = URL.createObjectURL(blob);
    $('qrImage').src = qrObjectUrl;
  }

  async function startPairing() {
    clearInterval(pollTimer);
    pairing = null;
    if (qrObjectUrl) { URL.revokeObjectURL(qrObjectUrl); qrObjectUrl = null; }
    $('qrImage').removeAttribute('src');
    setPairStatus('جارٍ إنشاء رمز ربط جديد...');
    try {
      if (!crypto.subtle) throw new Error('المتصفح لا يدعم Web Crypto. افتح Himo Web عبر HTTPS.');
      const qrKey = randomHex();
      const pollSecret = randomHex();
      const name = browserName();
      const [qrKeyHash, pollSecretHash] = await Promise.all([sha256Hex(qrKey), sha256Hex(pollSecret)]);
      const result = await request('/api/web/pair/start', {
        method: 'POST',
        body: JSON.stringify({ browserName: name, qrKeyHash, pollSecretHash })
      }, false);
      const pairId = result.pairingId;
      const qrPayload = `himo://link-device/${encodeURIComponent(pairId)}?key=${qrKey}&device=${encodeURIComponent(name)}`;
      await renderPairingQr(qrPayload);
      pairing = { pairId, pollSecret, expiresAt: Date.now() + 120000 };
      setPairStatus('انتظر موافقتك من تطبيق Himo على الهاتف.');
      pollTimer = setInterval(pollPairing, 2000);
    } catch (error) {
      setPairStatus(error.message || 'تعذر إنشاء رمز QR.', 'bad');
    }
  }
  async function pollPairing() {
    if (!pairing || pollInFlight) return;
    if (Date.now() >= pairing.expiresAt) {
      clearInterval(pollTimer);
      pairing = null;
      setPairStatus('انتهت صلاحية الرمز. أنشئ رمزًا جديدًا.', 'bad');
      return;
    }
    pollInFlight = true;
    try {
      const result = await request('/api/web/pair/poll', {
        method: 'POST',
        body: JSON.stringify({ pairingId: pairing.pairId, pollSecret: pairing.pollSecret })
      }, false);
      if (result.status === 'expired') {
        clearInterval(pollTimer);
        pairing = null;
        setPairStatus('انتهت صلاحية الرمز. أنشئ رمزًا جديدًا.', 'bad');
        return;
      }
      if (result.status === 'consumed') {
        clearInterval(pollTimer);
        pairing = null;
        setPairStatus('تم استهلاك طلب الربط. أنشئ رمزًا جديدًا إذا لم يكتمل الربط.', 'bad');
        return;
      }
      if (result.status === 'approved' && result.accessToken) {
        clearInterval(pollTimer);
        token = result.accessToken;
        localStorage.setItem(TOKEN_KEY, token);
        try {
          await request('/api/web/pair/complete', {
            method: 'POST',
            body: JSON.stringify({ pairingId: pairing.pairId, pollSecret: pairing.pollSecret })
          }, false);
        } catch { }
        pairing = null;
        await showApp();
      }
    } catch (error) {
      setPairStatus(error.message || 'تعذر التحقق من حالة الربط.', 'bad');
    } finally {
      pollInFlight = false;
    }
  }

  function showPairScreen() {
    clearInterval(pollTimer);
    $('app').style.display = 'none';
    $('pairScreen').style.display = 'grid';
    if (!pairing) startPairing();
  }
  async function showApp() {
    $('pairScreen').style.display = 'none';
    $('app').style.display = 'grid';
    try {
      const profile = await request('/api/me');
      window.himoUserId = profile.userId;
      $('accountName').textContent = profile.name || profile.email || 'حساب Himo';
      await loadConversations();
      clearInterval(pollTimer);
      pollTimer = setInterval(refreshActiveMessages, 3000);
    } catch (error) {
      console.error(error);
      showPairScreen();
    }
  }

  async function loadConversations() {
    conversations = await request('/api/conversations');
    renderConversations();
    if (activeConversation) {
      const stillExists = conversations.find(c => c.id === activeConversation.id);
      if (!stillExists) {
        activeConversation = null;
        $('chatHead').innerHTML = '<strong>اختر محادثة</strong>';
        $('messages').innerHTML = '<div class="empty">لم تعد هذه المحادثة متاحة.</div>';
        $('composer').style.display = 'none';
      }
    }
  }
  function renderConversations() {
    const list = $('conversationList');
    const query = $('conversationSearch').value.trim().toLowerCase();
    list.replaceChildren();
    for (const conversation of conversations.filter(c => (c.name || '').toLowerCase().includes(query))) {
      const item = document.createElement('div');
      item.className = `conv ${activeConversation?.id === conversation.id ? 'active' : ''}`;
      const avatar = document.createElement('div'); avatar.className = 'avatar'; avatar.textContent = (conversation.name || 'H').trim().slice(0, 1);
      const body = document.createElement('div'); body.style.minWidth = '0'; body.style.flex = '1';
      const name = document.createElement('div'); name.className = 'convname'; name.textContent = conversation.name || 'محادثة';
      const preview = document.createElement('div'); preview.className = 'preview'; preview.textContent = conversation.lastMessage || 'ابدأ المحادثة';
      body.append(name, preview); item.append(avatar, body);
      item.addEventListener('click', () => selectConversation(conversation));
      list.append(item);
    }
  }

  async function selectConversation(conversation) {
    activeConversation = conversation;
    renderConversations();
    $('chatHead').replaceChildren();
    const label = document.createElement('strong'); label.textContent = conversation.name || 'محادثة';
    $('chatHead').append(label);
    $('composer').style.display = 'flex';
    $('messages').replaceChildren();
    await loadMessages(true);
    try { await request(`/api/conversations/${conversation.id}/read`, { method: 'POST' }); } catch { }
    if (window.matchMedia('(max-width:760px)').matches) {
      $('sidebar').classList.remove('mobile-show');
      $('chatPanel').classList.remove('mobile-hide');
    }
  }

  async function loadMessages(scrollToBottom = false) {
    if (!activeConversation || isRefreshing) return;
    isRefreshing = true;
    try {
      const messages = await request(`/api/conversations/${activeConversation.id}/messages?limit=100`);
      const panel = $('messages');
      const oldHeight = panel.scrollHeight;
      const wasNearBottom = panel.scrollTop + panel.clientHeight >= oldHeight - 100;
      panel.replaceChildren();
      for (const message of messages) panel.append(await renderMessage(message));
      if (scrollToBottom || wasNearBottom) panel.scrollTop = panel.scrollHeight;
    } finally { isRefreshing = false; }
  }
  async function renderMessage(message) {
    const bubble = document.createElement('div');
    bubble.className = `bubble ${message.senderUserId === window.himoUserId ? 'mine' : ''}`;
    const text = document.createElement('div'); text.textContent = message.text || '';
    bubble.append(text);
    if (message.attachmentFileName) {
      const link = document.createElement('button');
      link.type = 'button'; link.className = 'attachment'; link.textContent = `📎 ${message.attachmentFileName} · تنزيل`;
      link.addEventListener('click', () => downloadAttachment(message));
      bubble.append(link);
      if ((message.attachmentContentType || '').startsWith('image/')) {
        const image = document.createElement('img'); image.className = 'media'; image.alt = message.attachmentFileName;
        bubble.append(image); loadAttachmentPreview(message, image, 'image');
      } else if ((message.attachmentContentType || '').startsWith('video/')) {
        const video = document.createElement('video'); video.className = 'media'; video.controls = true; video.playsInline = true;
        bubble.append(video); loadAttachmentPreview(message, video, 'video');
      } else if ((message.attachmentContentType || '').startsWith('audio/')) {
        const audio = document.createElement('audio'); audio.controls = true; audio.style.display = 'block'; audio.style.maxWidth = '100%'; bubble.append(audio);
        loadAttachmentPreview(message, audio, 'audio');
      }
    }
    const time = document.createElement('span'); time.className = 'time';
    try { time.textContent = new Date(message.sentAt).toLocaleString('ar-SA', { hour: '2-digit', minute: '2-digit' }); } catch { }
    bubble.append(time);
    return bubble;
  }
  async function getAttachmentBlob(message) {
    const response = await fetch(`/api/messages/${message.id}/attachment`, { headers: { Authorization: `Bearer ${token}` } });
    if (!response.ok) throw new Error('تعذر تحميل المرفق.');
    return response.blob();
  }
  async function getAttachmentObjectUrl(message) {
    const key = String(message.id);
    if (attachmentObjectUrls.has(key)) return attachmentObjectUrls.get(key);
    const blob = await getAttachmentBlob(message);
    const url = URL.createObjectURL(blob);
    attachmentObjectUrls.set(key, url);
    return url;
  }
  async function loadAttachmentPreview(message, element, kind) {
    try { element.src = await getAttachmentObjectUrl(message); }
    catch (error) { console.warn(error); }
  }
  async function downloadAttachment(message) {
    try {
      const url = await getAttachmentObjectUrl(message);
      const anchor = document.createElement('a'); anchor.href = url; anchor.download = message.attachmentFileName || 'himo-file'; anchor.click();
    } catch (error) { alert(error.message); }
  }
  function releaseAttachmentObjectUrls() {
    for (const url of attachmentObjectUrls.values()) URL.revokeObjectURL(url);
    attachmentObjectUrls.clear();
  }
  async function refreshActiveMessages() {
    try {
      await loadMessages(false);
      await loadConversations();
    } catch (error) { console.warn(error); }
  }
  async function sendMessage(text) {
    if (!activeConversation || !text.trim()) return;
    const input = $('messageInput');
    $('sendBtn').disabled = true;
    try {
      await request(`/api/conversations/${activeConversation.id}/messages`, {
        method: 'POST',
        body: JSON.stringify({ text: text.trim(), clientMessageId: crypto.randomUUID(), replyToMessageId: null })
      });
      input.value = '';
      await loadMessages(true);
      await loadConversations();
    } catch (error) { alert(error.message); }
    finally { $('sendBtn').disabled = false; input.focus(); }
  }
  async function uploadFile(file) {
    if (!activeConversation || !file) return;
    if (file.size > 100 * 1024 * 1024) { alert('الحد الأقصى لحجم الملف 100 ميجابايت.'); return; }
    const form = new FormData(); form.append('file', file, file.name);
    $('attachBtn').disabled = true;
    try {
      await request(`/api/conversations/${activeConversation.id}/attachments`, { method: 'POST', body: form });
      await loadMessages(true); await loadConversations();
    } catch (error) { alert(error.message); }
    finally { $('attachBtn').disabled = false; $('fileInput').value = ''; }
  }

  async function showDevices() {
    $('devicesModal').style.display = 'grid';
    const list = $('devicesList'); list.textContent = 'جارٍ التحميل...';
    try {
      const devices = await request('/api/me/devices'); list.replaceChildren();
      for (const device of devices) {
        const row = document.createElement('div'); row.className = 'device';
        const info = document.createElement('div');
        const name = document.createElement('strong'); name.textContent = device.deviceName || 'جهاز Himo';
        const meta = document.createElement('div'); meta.className = 'muted'; meta.style.fontSize = '11px'; meta.style.marginTop = '5px';
        meta.textContent = `${device.deviceType === 'web' ? 'متصفح' : 'هاتف'} · ${new Date(device.createdAt).toLocaleString('ar-SA')}`;
        info.append(name, meta); row.append(info);
        if (device.isCurrent) { const current = document.createElement('span'); current.className = 'pill'; current.textContent = 'هذا المتصفح'; row.append(current); }
        else {
          const revoke = document.createElement('button'); revoke.className = 'danger'; revoke.textContent = 'إلغاء';
          revoke.addEventListener('click', async () => {
            if (!confirm(`إلغاء ربط ${device.deviceName}؟`)) return;
            try { await request(`/api/me/devices/${encodeURIComponent(device.deviceId)}`, { method: 'DELETE' }); await showDevices(); }
            catch (error) { alert(error.message); }
          }); row.append(revoke);
        }
        list.append(row);
      }
      if (!devices.length) list.textContent = 'لا توجد أجهزة مرتبطة.';
    } catch (error) { list.textContent = error.message; }
  }
  async function logout() {
    try { await request('/api/auth/logout', { method: 'POST' }); } catch { }
    token = ''; localStorage.removeItem(TOKEN_KEY); activeConversation = null; conversations = [];
    releaseAttachmentObjectUrls();
    $('devicesModal').style.display = 'none'; showPairScreen();
  }

  $('refreshQr').addEventListener('click', startPairing);
  $('conversationSearch').addEventListener('input', renderConversations);
  $('composer').addEventListener('submit', event => { event.preventDefault(); sendMessage($('messageInput').value); });
  $('attachBtn').addEventListener('click', () => $('fileInput').click());
  $('fileInput').addEventListener('change', event => uploadFile(event.target.files?.[0]));
  $('devicesBtn').addEventListener('click', showDevices);
  $('closeDevices').addEventListener('click', () => $('devicesModal').style.display = 'none');
  $('devicesModal').addEventListener('click', event => { if (event.target === $('devicesModal')) $('devicesModal').style.display = 'none'; });
  $('logoutBtn').addEventListener('click', logout);
  $('messageInput').addEventListener('keydown', event => {
    if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); $('composer').requestSubmit(); }
  });

  if (token) showApp(); else showPairScreen();
})();
