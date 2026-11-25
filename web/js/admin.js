(() => {
  let adminPin = localStorage.getItem('tres_admin_pin') || '';
  const el = {
    authOverlay: document.getElementById('admin-auth-overlay'),
    dashboard: document.getElementById('admin-dashboard'),
    pinInput: document.getElementById('admin-pin-input'),
    btnLogin: document.getElementById('btn-admin-login'),
    statActive: document.getElementById('stat-active-count'),
    statPending: document.getElementById('stat-pending-count'),
    statRevenue: document.getElementById('stat-revenue-total'),
    pendingList: document.getElementById('pending-list-container'),
    leasesList: document.getElementById('leases-list-container'),
    cashName: document.getElementById('cash-tenant-name'),
    cashPhone: document.getElementById('cash-tenant-phone'),
    btnAddCash: document.getElementById('btn-add-cash'),
    toast: document.getElementById('admin-toast')
  };

  function showToast(msg) {
    el.toast.textContent = msg;
    el.toast.classList.add('show');
    setTimeout(() => el.toast.classList.remove('show'), 2600);
  }

  function normalizePhone(raw) {
    let d = (raw || '').replace(/\D/g, '');
    if (d.startsWith('639') && d.length === 12) d = '0' + d.slice(2);
    return d;
  }

  async function tryLogin(pin) {
    try {
      const res = await fetch('/api/admin/leases', { headers: { 'X-Admin-PIN': pin } });
      if (res.status === 401) {
        showToast('Invalid Admin PIN.');
        localStorage.removeItem('tres_admin_pin');
        return false;
      }
      adminPin = pin;
      localStorage.setItem('tres_admin_pin', pin);
      el.authOverlay.style.display = 'none';
      el.dashboard.style.display = 'flex';
      await refreshData();
      setInterval(refreshData, 10000);
      return true;
    } catch {
      showToast('Gateway unreachable.');
      return false;
    }
  }

  el.btnLogin.addEventListener('click', () => tryLogin(el.pinInput.value.trim()));
  el.pinInput.addEventListener('keydown', e => { if (e.key === 'Enter') tryLogin(el.pinInput.value.trim()); });
  if (adminPin) tryLogin(adminPin);

  async function refreshData() {
    try {
      const [pRes, lRes] = await Promise.all([
        fetch('/api/admin/pending', { headers: { 'X-Admin-PIN': adminPin } }),
        fetch('/api/admin/leases', { headers: { 'X-Admin-PIN': adminPin } })
      ]);
      if (!pRes.ok || !lRes.ok) return;
      const pending = await pRes.json();
      const leasesData = await lRes.json();

      const leases = leasesData.leases || [];
      const activeCount = leases.filter(l => l.status === 'ACTIVE').length;
      el.statActive.textContent = activeCount;
      el.statPending.textContent = (pending || []).length;
      el.statRevenue.textContent = `₱${leasesData.monthly_revenue || 0}`;

      renderPending(pending);
      renderLeases(leases);
    } catch {}
  }

  function renderPending(pending) {
    el.pendingList.innerHTML = '';
    if (!pending || pending.length === 0) {
      el.pendingList.innerHTML = `<p class="emergency-sub" style="text-align:center; padding:8px;">All GCash payments verified.</p>`;
      return;
    }
    pending.forEach(item => {
      const card = document.createElement('div');
      card.className = 'pending-item';
      card.innerHTML = `
        <div class="card-top-row">
          <strong>${item.tenant_name || 'Tenant'} • ${item.phone_number || 'No Phone'}</strong>
          <span class="rate-badge">${item.ref_no}</span>
        </div>
        <button class="btn-approve">Approve 30 Days (₱200)</button>
      `;
      card.querySelector('.btn-approve').addEventListener('click', () => handleApprove(item.mac));
      el.pendingList.appendChild(card);
    });
  }

  function renderLeases(leases) {
    el.leasesList.innerHTML = '';
    if (!leases || leases.length === 0) {
      el.leasesList.innerHTML = `<p class="emergency-sub" style="text-align:center; padding:8px;">No subscribers yet.</p>`;
      return;
    }
    leases.forEach(l => {
      const isActive = l.status === 'ACTIVE';
      let rem = l.status;
      if (isActive && l.expires_at) {
        const diff = new Date(l.expires_at) - new Date();
        if (diff > 0) rem = `${Math.floor(diff/86400000)}d ${Math.floor((diff/3600000)%24)}h left`;
      }
      const recoveryTag = l.recovery_requested ? `<span class="rate-badge" style="background:var(--c-amber-dim); color:var(--c-amber);">PIN Recovery Requested</span>` : '';
      const card = document.createElement('div');
      card.className = 'lease-item-card';
      card.innerHTML = `
        <div style="display:flex; flex-direction:column; gap:3px;">
          <div style="display:flex; align-items:center; gap:6px; flex-wrap:wrap;">
            <strong>${l.tenant_name || 'Subscriber'}</strong>
            <span class="rate-badge ${isActive ? 'black' : ''}">${isActive ? 'Online' : l.status}</span>
            ${recoveryTag}
          </div>
          <span class="emergency-sub">${l.phone_number || 'No Phone'} • ${rem}</span>
          <span class="emergency-sub" style="font-family:monospace;">PIN: <strong>${l.pin_code || '----'}</strong> • MAC: ${l.mac_address}</span>
        </div>
        ${isActive ? `<button class="btn-revoke">Turn Off</button>` : ''}
      `;
      if (isActive) card.querySelector('.btn-revoke').addEventListener('click', () => handleRevoke(l.mac_address, l.tenant_name));
      el.leasesList.appendChild(card);
    });
  }

  async function handleApprove(mac) {
    const res = await fetch('/api/admin/approve', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Admin-PIN': adminPin },
      body: JSON.stringify({ mac })
    });
    const data = await res.json();
    if (data.success) {
      showToast(`Approved! New PIN: ${data.pin}`);
      await refreshData();
    } else {
      showToast(data.error || 'Approval failed.');
    }
  }

  el.btnAddCash.addEventListener('click', async () => {
    const name = el.cashName.value.trim();
    const phone = normalizePhone(el.cashPhone.value);
    if (name.length < 2 || !/^09\d{9}$/.test(phone)) {
      showToast('Enter tenant name and 09XXXXXXXXX phone.');
      return;
    }
    el.btnAddCash.disabled = true;
    try {
      const res = await fetch('/api/admin/add-cash', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-Admin-PIN': adminPin },
        body: JSON.stringify({ tenant_name: name, phone_number: phone })
      });
      const data = await res.json();
      if (data.success) {
        showToast(`Cash pass active! PIN: ${data.pin}`);
        el.cashName.value = '';
        el.cashPhone.value = '';
        await refreshData();
      } else {
        showToast(data.error || 'Failed.');
      }
    } finally {
      el.btnAddCash.disabled = false;
    }
  });

  async function handleRevoke(mac, name) {
    if (!confirm(`Turn off Wi-Fi for ${name || mac}?`)) return;
    const res = await fetch('/api/admin/revoke', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Admin-PIN': adminPin },
      body: JSON.stringify({ mac })
    });
    const data = await res.json();
    if (data.success) { showToast('Access turned off.'); await refreshData(); }
  }
})();
