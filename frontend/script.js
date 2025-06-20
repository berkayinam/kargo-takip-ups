const apiUrl = '/api/kargo';

function showLoading() {
    document.getElementById('loadingOverlay').style.display = 'flex';
}

function hideLoading() {
    document.getElementById('loadingOverlay').style.display = 'none';
}

async function fetchKargolar() {
    showLoading();
    try {
        const res = await fetch(apiUrl);
        const data = await res.json();
        const tbody = document.querySelector("#kargoTable tbody");
        tbody.innerHTML = "";
        data.forEach(k => {
            const row = document.createElement("tr");
            row.className = "hover:bg-primary/10 dark:hover:bg-primary-dark/20 transition";
            row.innerHTML = `
                <td class="py-2 px-2 border-r border-gray-300 dark:border-gray-600 last:border-r-0">
                    <a href="https://www.ups.com.tr/WaybillSorgu.aspx?Waybill=${k.takipNo}" target="_blank" class="text-primary underline hover:text-primary-dark">${k.takipNo}</a>
                </td>
                <td class="py-2 px-2 border-r border-gray-300 dark:border-gray-600 last:border-r-0">${k.magazaId || ''}</td>
                <td class="py-2 px-2 border-r border-gray-300 dark:border-gray-600 last:border-r-0">
                    <a href="https://gratis-it.4me.com/requests/${k.talepId}" target="_blank" class="text-primary underline hover:text-primary-dark">${k.talepId}</a>
                </td>
                <td class="py-2 px-2 border-r border-gray-300 dark:border-gray-600 last:border-r-0">${k.talepAdi || ''}</td>
                <td class="py-2 px-2 border-r border-gray-300 dark:border-gray-600 last:border-r-0">
                    <span class="inline-block px-2 py-1 rounded text-xs font-semibold
                        ${k.durum === 'Teslim Edildi'
                            ? 'bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-200'
                            : 'bg-yellow-100 text-yellow-800 dark:bg-yellow-900 dark:text-yellow-200'}">
                        ${k.durum || ''}
                    </span>
                </td>
                <td class="py-2 px-2 border-r border-gray-300 dark:border-gray-600 last:border-r-0">${k.ongorulenTeslimat || ''}</td>
                <td class="py-2 px-2 border-r border-gray-300 dark:border-gray-600 last:border-r-0">${new Date(k.sonGuncelleme).toLocaleString('tr-TR')}</td>
                <td class="py-2 px-2 border-r border-gray-300 dark:border-gray-600 last:border-r-0">
                    <button onclick="deleteKargo('${k.takipNo}')" class="bg-primary hover:bg-primary-dark text-white px-3 py-1 rounded shadow text-sm">Sil</button>
                </td>
            `;
            tbody.appendChild(row);
        });

        // Toplam talep sayısını göster
        document.getElementById("talepSayisi").textContent = `Toplam Talep: ${data.length}`;
    } catch (error) {
        alert('Kargolar yüklenirken bir hata oluştu: ' + error.message);
    } finally {
        hideLoading();
    }
}

async function deleteKargo(takipNo) {
    showLoading();
    try {
        const response = await fetch(`${apiUrl}/${takipNo}`, { method: "DELETE" });
        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(errorText);
        }
        await fetchKargolar();
    } catch (error) {
        alert('Kargo silinirken bir hata oluştu: ' + error.message);
    } finally {
        hideLoading();
    }
}

async function checkStatus(trackingNumber) {
    showLoading();
    try {
        const response = await fetch(`${apiUrl}/check-status/${trackingNumber}`, {
            method: 'POST'
        });
        
        if (!response.ok) {
            throw new Error('Kargo durumu kontrol edilirken hata oluştu');
        }
        
        const result = await response.json();
        if (result.success) {
            fetchKargolar(); // Tabloyu yenile
        } else {
            alert(result.message);
        }
    } catch (error) {
        alert(error.message);
    } finally {
        hideLoading();
    }
}

document.getElementById("kargoForm").addEventListener("submit", async (e) => {
    e.preventDefault();
    showLoading();
    const kargo = {
        TakipNo: document.getElementById("takipNo").value,
        MagazaID: document.getElementById("magazaID").value,
        TalepID: document.getElementById("talepID").value,
        TeslimEdildi: false,
        OngorulenTeslimat: "-",
        LastUpdate: new Date().toISOString()
    };
    try {
        const response = await fetch(apiUrl, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(kargo)
        });
        
        if (!response.ok) {
            if (response.status === 409) {
                alert("Bu takip numarası zaten sistemde kayıtlı.");
            } else {
                alert("Bir hata oluştu.");
            }
        } else {
            fetchKargolar();
        }
    } catch (error) {
        alert('Kargo eklenirken bir hata oluştu: ' + error.message);
    } finally {
        hideLoading();
    }
});

document.getElementById("loadFrom4me").addEventListener("click", async () => {
    showLoading();
    try {
        const response = await fetch(`${apiUrl}/load-from-4me`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({})
        });
        const result = await response.json();
        if (result.success) {
            alert(result.message);
            fetchKargolar();
        } else {
            throw new Error(result.message);
        }
    } catch (error) {
        alert('4me\'den veri yüklenirken bir hata oluştu: ' + error.message);
    } finally {
        hideLoading();
    }
});

document.addEventListener('DOMContentLoaded', function() {
    const deleteAllButton = document.getElementById('deleteAllKargos');
    if (deleteAllButton) {
        deleteAllButton.addEventListener('click', async () => {
            console.log('Tümünü Sil butonuna tıklandı.');
            if (confirm("Tüm kargoları silmek istediğinize emin misiniz?")) {
                showLoading();
                try {
                    const response = await fetch('/api/kargo/delete-all', {
                        method: 'DELETE'
                    });
                    if (!response.ok) {
                        const errorText = await response.text();
                        throw new Error(errorText);
                    }
                    await fetchKargolar();
                } catch (error) {
                    alert('Tüm kargolar silinirken bir hata oluştu: ' + error.message);
                } finally {
                    hideLoading();
                }
            }
        });
    }

    const updateButton = document.getElementById('updateKargos');
    const loadingOverlay = document.getElementById('loadingOverlay');
    let isUpdating = false;

    updateButton.addEventListener('click', async function() {
        if (isUpdating) return;
        
        isUpdating = true;
        loadingOverlay.style.display = 'flex';
        updateButton.disabled = true;

        try {
            const response = await fetch('/api/kargo/update-all', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                }
            });

            const result = await response.json();
            
            if (result.success) {
                // Refresh the table with new data
                await fetchKargolar();
                alert('Kargolar başarıyla güncellendi!');
            } else {
                alert('Hata: ' + result.message);
            }
        } catch (error) {
            alert('Bir hata oluştu: ' + error.message);
        } finally {
            isUpdating = false;
            loadingOverlay.style.display = 'none';
            updateButton.disabled = false;
        }
    });

    // Function to load kargos
    async function loadKargos() {
        try {
            const response = await fetch('/api/kargo');
            const kargolar = await response.json();
            
            const tbody = document.querySelector('#kargoTable tbody');
            tbody.innerHTML = '';
            
            kargolar.forEach(kargo => {
                const row = document.createElement('tr');
                row.className = "hover:bg-primary/10 dark:hover:bg-primary-dark/20 transition";
                row.innerHTML = `
                    <td class="py-2 px-2 border-r border-gray-300 dark:border-gray-600 last:border-r-0">
                        <a href="https://www.ups.com.tr/WaybillSorgu.aspx?Waybill=${kargo.takipNo}" target="_blank" class="text-primary underline hover:text-primary-dark">${kargo.takipNo}</a>
                    </td>
                    <td class="py-2 px-2 border-r border-gray-300 dark:border-gray-600 last:border-r-0">${kargo.magazaId || ''}</td>
                    <td class="py-2 px-2 border-r border-gray-300 dark:border-gray-600 last:border-r-0">
                        <a href="https://gratis-it.4me.com/requests/${kargo.talepId}" target="_blank" class="text-primary underline hover:text-primary-dark">${kargo.talepId}</a>
                    </td>
                    <td class="py-2 px-2 border-r border-gray-300 dark:border-gray-600 last:border-r-0">${kargo.talepAdi || ''}</td>
                    <td class="py-2 px-2 border-r border-gray-300 dark:border-gray-600 last:border-r-0">
                        <span class="inline-block px-2 py-1 rounded text-xs font-semibold
                            ${kargo.durum === 'Teslim Edildi'
                                ? 'bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-200'
                                : 'bg-yellow-100 text-yellow-800 dark:bg-yellow-900 dark:text-yellow-200'}">
                            ${kargo.durum || ''}
                        </span>
                    </td>
                    <td class="py-2 px-2 border-r border-gray-300 dark:border-gray-600 last:border-r-0">${kargo.ongorulenTeslimat || ''}</td>
                    <td class="py-2 px-2 border-r border-gray-300 dark:border-gray-600 last:border-r-0">${new Date(kargo.sonGuncelleme).toLocaleString('tr-TR')}</td>
                    <td class="py-2 px-2 border-r border-gray-300 dark:border-gray-600 last:border-r-0">
                        <button onclick="deleteKargo('${kargo.takipNo}')" class="bg-primary hover:bg-primary-dark text-white px-3 py-1 rounded shadow text-sm">Sil</button>
                    </td>
                `;
                tbody.appendChild(row);
            });

            document.getElementById('talepSayisi').textContent = `Toplam Talep: ${kargolar.length}`;
        } catch (error) {
            console.error('Kargolar yüklenirken hata oluştu:', error);
        }
    }

    // Initial load
    loadKargos();
});

fetchKargolar();