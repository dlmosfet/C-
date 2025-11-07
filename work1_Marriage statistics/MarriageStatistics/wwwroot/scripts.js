// 主題切換
(() => {
    'use strict'
    
    const getStoredTheme = () => localStorage.getItem('theme')
    const setStoredTheme = theme => localStorage.setItem('theme', theme)

    const getPreferredTheme = () => {
        const storedTheme = getStoredTheme()
        if (storedTheme) {
            return storedTheme
        }
        return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
    }

    const setTheme = theme => {
        document.documentElement.setAttribute('data-bs-theme', theme)
    }

    setTheme(getPreferredTheme())

    const showActiveTheme = (theme, focus = false) => {
        const themeSwitcher = document.querySelector('#bd-theme')
        if (!themeSwitcher) {
            return
        }
        const themeSwitcherText = document.querySelector('#bd-theme-text')
        const activeThemeIcon = document.querySelector('.theme-icon-active use')
        const btnToActive = document.querySelector(`[data-bs-theme-value="${theme}"]`)
        const svgOfActiveBtn = btnToActive.querySelector('i').getAttribute('class')

        document.querySelectorAll('[data-bs-theme-value]').forEach(element => {
            element.classList.remove('active')
            element.setAttribute('aria-pressed', 'false')
        })

        btnToActive.classList.add('active')
        btnToActive.setAttribute('aria-pressed', 'true')
        activeThemeIcon.setAttribute('href', svgOfActiveBtn)
        const themeSwitcherLabel = `${themeSwitcherText.textContent} (${btnToActive.dataset.bsThemeValue})`
        themeSwitcher.setAttribute('aria-label', themeSwitcherLabel)

        if (focus) {
            themeSwitcher.focus()
        }
    }

    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
        const storedTheme = getStoredTheme()
        if (!storedTheme) {
            setTheme(getPreferredTheme())
        }
    })

    document.querySelectorAll('[data-bs-theme-value]')
        .forEach(toggle => {
            toggle.addEventListener('click', () => {
                const theme = toggle.getAttribute('data-bs-theme-value')
                setStoredTheme(theme)
                setTheme(theme)
                showActiveTheme(theme, true)
            })
        })
})()

// 視圖切換
document.querySelectorAll('[data-view]').forEach(link => {
    link.addEventListener('click', (e) => {
        e.preventDefault()
        const viewId = link.getAttribute('data-view')
        document.querySelectorAll('.view-section').forEach(section => {
            section.classList.add('d-none')
        })
        document.getElementById(`${viewId}-view`).classList.remove('d-none')
        
        // 更新導航狀態
        document.querySelectorAll('.nav-link').forEach(navLink => {
            navLink.classList.remove('active')
        })
        link.classList.add('active')
    })
})

// 圖表初始化
const initCharts = () => {
    // 趨勢圖
    const trendCtx = document.getElementById('trend-chart').getContext('2d')
    new Chart(trendCtx, {
        type: 'line',
        data: {
            labels: [],
            datasets: [{
                label: '結婚對數',
                data: [],
                borderColor: 'rgb(var(--bs-primary-rgb))',
                tension: 0.1
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false
        }
    })

    // 區域分佈圖
    const areaChart = echarts.init(document.getElementById('area-chart'))
    areaChart.setOption({
        tooltip: {
            trigger: 'item'
        },
        series: [{
            type: 'pie',
            radius: ['40%', '70%'],
            data: []
        }]
    })

    // 性別比例圖
    const genderCtx = document.getElementById('gender-chart').getContext('2d')
    new Chart(genderCtx, {
        type: 'doughnut',
        data: {
            labels: ['同性別', '不同性別'],
            datasets: [{
                data: [0, 0],
                backgroundColor: [
                    'rgba(var(--bs-primary-rgb), 0.8)',
                    'rgba(var(--bs-primary-rgb), 0.4)'
                ]
            }]
        }
    })

    // 年齡層分布圖
    const ageCtx = document.getElementById('age-chart').getContext('2d')
    new Chart(ageCtx, {
        type: 'bar',
        data: {
            labels: [],
            datasets: [{
                label: '數量',
                data: [],
                backgroundColor: 'rgba(var(--bs-primary-rgb), 0.6)'
            }]
        }
    })
}

// API 呼叫
const api = {
    async fetch(url) {
        throw new Error('Manual fetch endpoint has been removed. Background fetcher runs periodically.');
    },

    async getEntries() {
        const res = await fetch('/api/entries')
        return res.json()
    },

    async getStats() {
        const res = await fetch('/api/stats')
        return res.json()
    }
}

// 初始化
document.addEventListener('DOMContentLoaded', () => {
    initCharts()
    
    // 初始載入儀表板資料
    api.getStats().then(stats => {
        // total-fetches stat removed; use lastUpdate and marriage totals
        const lu = stats.lastUpdate || stats.last_update || null;
        if (lu) document.getElementById('last-update').textContent = new Date(lu).toLocaleString()
        document.getElementById('total-marriages').textContent = stats.totalMarriages ?? stats.total_marriages ?? 0
        document.getElementById('monthly-change').textContent = `${stats.monthlyChange ?? stats.monthly_change ?? 0}%`
    })

    // Manual fetch UI removed; background fetcher runs periodically on the server.

    // 初始化載入歷史記錄
    updateFetchHistory()
})

// 更新抓取歷史
async function updateFetchHistory() {
    const tbody = document.querySelector('#fetch-history tbody')
    const entries = await api.getEntries()
    
    tbody.innerHTML = entries.map(entry => `
        <tr>
            <td>${entry.id}</td>
            <td>${entry.source}</td>
            <td>${new Date(entry.timestamp).toLocaleString()}</td>
            <td>
                <button class="btn btn-sm btn-outline-primary" onclick="viewDetails(${entry.id})">
                    <i class="bi bi-eye"></i>
                </button>
            </td>
        </tr>
    `).join('')
}

// 查看詳細資料
function viewDetails(id) {
    // TODO: 實作詳細資料檢視
    console.log(`查看 ID: ${id} 的詳細資料`)
}