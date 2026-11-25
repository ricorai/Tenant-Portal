(() => {
  const urlParams = new URLSearchParams(window.location.search);
  const macOverride = urlParams.get('mac') || '';

  const views = {
    details: document.getElementById('view-details'),
    paywall: document.getElementById('view-paywall'),
    pin: document.getElementById('view-pin'),
    dashboard: document.getElementById('view-dashboard')
  };

  let currentStatus = null;
  let countdownInterval = null;
  let emergencyInterval = null;

  function normalizePhone(raw) {
    let digits = (raw || '').replace(/\D/g, '');
    if (digits.startsWith('639') && digits.length === 12) digits = '0' + digits.slice(2);
    return digits;
  }

  function showToast(msg) {
    const t = document.getElementById('toast-pill');
    t.textContent = msg;
    t.classList.add('show');
    setTimeout(() => t.classList.remove('show'), 2600);
  }

  function showView(name) {
    Object.keys(views).forEach(k => {
      views[k].style.display = k === name ? 'flex' : 'none';
    });
    document.getElementById('page-title').textContent = name === 'pin' ? 'Tenant Login' : 'Tenant Wi-Fi';
  }

  function setWifiVisual(state) {
    const icon = document.getElementById('wifi-icon');
    const badge = document.getElementById('status-badge');
    const dot = document.getElementById('status-dot');
    const text = document.getElementById('status-text');
    const bars = icon.querySelectorAll('path, circle');

    icon.classList.remove('red', 'green');
    badge.classList.remove('online', 'pending');
    dot.classList.remove('online', 'pending');

    if (state === 'ACTIVE') {
      icon.classList.add('green');
      badge.classList.add('online');
      dot.classList.add('online');
      text.textContent = 'Online';
      bars.forEach((b, i) => b.setAttribute('class', `wifi-bar wifi-bar-${4 - i}`));
    } else if (state === 'PENDING') {
      icon.classList.add('red');
      badge.classList.add('pending');
      dot.classList.add('pending');
      text.textContent = 'Pending Approval';
      bars.forEach(b => b.setAttribute('class', ''));
    } else {
      icon.classList.add('red');
      text.textContent = 'No Internet Access';
      bars.forEach(b => b.setAttribute('class', ''));
    }
  }

  function startLeaseCountdown(expiresIso) {
    if (countdownInterval) clearInterval(countdownInterval);
    const display = document.getElementById('countdown-display');
    const expLabel = document.getElementById('countdown-expiry');
    if (!expiresIso) { display.textContent = 'Active'; return; }

    const expDate = new Date(expiresIso);
    expLabel.textContent = `Expires ${expDate.toLocaleDateString()} • Renew after expiry`;

    const tick = () => {
      const diff = expDate - new Date();
      if (diff <= 0) {
        clearInterval(countdownInterval);
        fetchStatus();
        return;
      }
      const d = Math.floor(diff / 86400000);
      const h = Math.floor((diff / 3600000) % 24);
      const m = Math.floor((diff / 60000) % 60);
      const s = Math.floor((diff / 1000) % 60);
      display.textContent = `${d}d ${String(h).padStart(2,'0')}h ${String(m).padStart(2,'0')}m ${String(s).padStart(2,'0')}s`;
    };
    tick();
    countdownInterval = setInterval(tick, 1000);
  }

  function startEmergencyTimer(secondsLeft) {
    if (emergencyInterval) clearInterval(emergencyInterval);
    const btn = document.getElementById('btn-emergency');
    const banner = document.getElementById('emergency-timer-banner');
    const digits = document.getElementById('timer-digits');
    btn.style.display = 'none';
    banner.classList.add('show');

    let sec = Math.max(0, Math.floor(secondsLeft));
    const tick = () => {
      if (sec <= 0) {
        clearInterval(emergencyInterval);
        banner.classList.remove('show');
        btn.style.display = 'block';
        btn.disabled = true;
        btn.textContent = 'Daily 10-Min Pass Used';
        return;
      }
      digits.textContent = `${String(Math.floor(sec / 60)).padStart(2,'0')}:${String(sec % 60).padStart(2,'0')}`;
      sec--;
    };
    tick();
    emergencyInterval = setInterval(tick, 1000);
  }

  async function fetchStatus(preferredView) {
    try {
      const q = macOverride ? `?mac=${encodeURIComponent(macOverride)}` : '';
      const res = await fetch(`/api/status${q}`);
      const data = await res.json();
      currentStatus = data;

      setWifiVisual(data.status);

      if (data.tenant_name) localStorage.setItem('tres_tenant_name', data.tenant_name);
      if (data.phone_number) localStorage.setItem('tres_tenant_phone', data.phone_number);

      if (data.status === 'ACTIVE') {
        document.getElementById('dash-tenant-badge').textContent = data.tenant_name || 'Active Tenant';
        document.getElementById('recovery-pin-display').textContent = data.pin || '----';
        startLeaseCountdown(data.expires_at);
        showView('dashboard');
        return;
      }

      // Not active: hydrate Details inputs
      const savedName = localStorage.getItem('tres_tenant_name') || '';
      const savedPhone = localStorage.getItem('tres_tenant_phone') || '';
      if (savedName) document.getElementById('input-name').value = savedName;
      if (savedPhone) document.getElementById('input-phone').value = savedPhone;
      document.getElementById('tenant-chip').textContent = `${savedName || 'Guest'} • ${savedPhone || '09XXXXXXXXX'}`;

      // Emergency Pass State
      const btnEmerg = document.getElementById('btn-emergency');
      if (data.emergency_active && data.emergency_seconds_left > 0) {
        startEmergencyTimer(data.emergency_seconds_left);
      } else if (data.emergency_passes_today >= 1) {
        btnEmerg.disabled = true;
        btnEmerg.textContent = 'Daily 10-Min Pass Used (0 left today)';
      }

      // Pending Payment Notice
      const pendingNotice = document.getElementById('pending-notice');
      if (data.status === 'PENDING') {
        pendingNotice.style.display = 'flex';
        document.getElementById('pending-ref-display').textContent = data.paid_reference || 'Submitted';
        showView('paywall');
        return;
      } else {
        pendingNotice.style.display = 'none';
      }

      if (preferredView) {
        showView(preferredView);
      } else if (savedName && /^09\d{9}$/.test(savedPhone) && urlParams.get('step') === 'pay') {
        showView('paywall');
      } else {
        showView('details');
      }
    } catch (err) {
      showView(preferredView || 'details');
    }
  }

  // Details -> Paywall
  document.getElementById('btn-to-payment').addEventListener('click', () => {
    const name = document.getElementById('input-name').value.trim();
    const phone = normalizePhone(document.getElementById('input-phone').value);
    const err = document.getElementById('details-error');
    if (name.length < 2 || !/^09\d{9}$/.test(phone)) {
      err.classList.add('show');
      return;
    }
    err.classList.remove('show');
    localStorage.setItem('tres_tenant_name', name);
    localStorage.setItem('tres_tenant_phone', phone);
    document.getElementById('tenant-chip').textContent = `${name} • ${phone}`;
    showView('paywall');
  });

  document.getElementById('btn-back-details').addEventListener('click', () => showView('details'));
  document.getElementById('btn-go-pin').addEventListener('click', () => showView('pin'));
  document.getElementById('btn-paywall-to-pin').addEventListener('click', () => showView('pin'));
  document.getElementById('btn-pin-to-details').addEventListener('click', () => showView('details'));

  // 10-Min Emergency Pass
  document.getElementById('btn-emergency').addEventListener('click', async () => {
    const btn = document.getElementById('btn-emergency');
    btn.disabled = true;
    try {
      const res = await fetch('/api/emergency-pass', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ mac: macOverride })
      });
      const data = await res.json();
      if (data.success) {
        showToast('10-Minute Pass (1 Mbps) Activated!');
        startEmergencyTimer(data.seconds_left || 600);
      } else {
        showToast(data.error || 'Daily 10-Min Pass limit reached.');
        btn.disabled = true;
      }
    } catch {
      showToast('Gateway unreachable.');
      btn.disabled = false;
    }
  });

  // GCash Helpers
  document.getElementById('btn-copy-num').addEventListener('click', () => {
    navigator.clipboard?.writeText('09XXXXXXXXX');
    showToast('Copied: 09XX-XXX-XXXX (JU** DE*A C***)');
  });
  document.getElementById('btn-open-gcash').addEventListener('click', () => {
    showToast('Opening GCash...');
    setTimeout(() => { window.location.href = 'gcash://'; }, 300);
  });
  document.getElementById('btn-save-qr').addEventListener('click', () => {
    const a = document.createElement('a');
    a.href = 'images/gcash_qr.png';
    a.download = 'TresRentals_GCash_QR.png';
    a.click();
    showToast('QR Code saved to downloads!');
  });

  // Submit Payment
  document.getElementById('btn-main-submit').addEventListener('click', async () => {
    const name = localStorage.getItem('tres_tenant_name') || document.getElementById('input-name').value.trim();
    const phone = normalizePhone(localStorage.getItem('tres_tenant_phone') || document.getElementById('input-phone').value);
    const refNo = document.getElementById('ref-input-field').value.trim();
    const btn = document.getElementById('btn-main-submit');

    if (name.length < 2 || !/^09\d{9}$/.test(phone)) {
      showToast('Please enter your Name & 09... number first.');
      showView('details');
      return;
    }

    btn.disabled = true;
    btn.textContent = 'Submitting...';
    try {
      const res = await fetch('/api/submit-payment', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ mac: macOverride, tenant_name: name, phone_number: phone, ref_no: refNo })
      });
      const data = await res.json();
      if (data.success) {
        document.getElementById('success-title').textContent = 'Payment Submitted!';
        document.getElementById('success-desc').textContent = data.message || 'Pending Admin approval.';
        document.getElementById('success-modal').classList.add('active');
        setTimeout(() => {
          document.getElementById('success-modal').classList.remove('active');
          fetchStatus('paywall');
        }, 1600);
      } else {
        showToast(data.error || 'Submission failed.');
      }
    } catch {
      showToast('Error connecting to gateway.');
    } finally {
      btn.disabled = false;
      btn.textContent = 'Submit & Connect';
    }
  });

  // PIN Boxes UX
  const pinBoxes = document.querySelectorAll('.pin-digit-box');
  pinBoxes.forEach((box, idx) => {
    box.addEventListener('input', () => {
      box.value = box.value.replace(/\D/g, '').slice(0, 1);
      if (box.value && idx < 3) pinBoxes[idx + 1].focus();
    });
    box.addEventListener('keydown', e => {
      if (e.key === 'Backspace' && !box.value && idx > 0) pinBoxes[idx - 1].focus();
    });
  });

  // Relink PIN Login
  document.getElementById('btn-pin-login').addEventListener('click', async () => {
    const pin = Array.from(pinBoxes).map(b => b.value).join('');
    if (pin.length !== 4) { showToast('Enter all 4 PIN digits.'); return; }
    const btn = document.getElementById('btn-pin-login');
    btn.disabled = true;
    btn.textContent = 'Verifying PIN...';
    try {
      const res = await fetch('/api/relink-pin', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ pin, new_mac: macOverride })
      });
      const data = await res.json();
      if (data.success) {
        document.getElementById('success-title').textContent = 'PIN Verified!';
        document.getElementById('success-desc').textContent = 'Opening your dashboard...';
        document.getElementById('success-modal').classList.add('active');
        setTimeout(() => {
          document.getElementById('success-modal').classList.remove('active');
          fetchStatus();
        }, 1400);
      } else {
        showToast(data.error || 'Invalid PIN.');
      }
    } catch {
      showToast('Gateway error.');
    } finally {
      btn.disabled = false;
      btn.textContent = 'Connect to Wi-Fi';
    }
  });

  // PIN Recovery Request
  document.getElementById('btn-toggle-recovery').addEventListener('click', () => {
    document.getElementById('recovery-drawer').classList.toggle('show');
  });
  document.getElementById('btn-request-recovery').addEventListener('click', async () => {
    const phone = normalizePhone(document.getElementById('recover-phone').value);
    const msgEl = document.getElementById('recovery-msg');
    msgEl.style.display = 'block';
    if (!/^09\d{9}$/.test(phone)) {
      msgEl.style.color = '#EF4444';
      msgEl.textContent = 'Enter a valid 11-digit 09XXXXXXXXX mobile number.';
      return;
    }
    try {
      const res = await fetch('/api/request-recovery', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ phone_number: phone })
      });
      const data = await res.json();
      msgEl.style.color = data.success ? '#166534' : '#EF4444';
      msgEl.textContent = data.message || data.error || 'Recovery requested.';
    } catch {
      msgEl.style.color = '#EF4444';
      msgEl.textContent = 'Could not reach gateway.';
    }
  });

  // Dashboard Actions
  document.getElementById('btn-copy-pin').addEventListener('click', () => {
    const pin = document.getElementById('recovery-pin-display').textContent;
    navigator.clipboard?.writeText(pin);
    showToast(`Copied Subscription PIN: ${pin}`);
  });
  document.getElementById('btn-diagnostics').addEventListener('click', () => {
    showToast('Ping: 8ms • Jitter: 0.8ms • Bufferbloat Grade A+');
  });

  fetchStatus();
})();
