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
                options: {
                    animation: {
                        duration: 1000
                    }
                },
                data: [],
                borderColor: 'rgb(var(--bs-primary-rgb))',
                tension: 0.1,
                fill: false
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: true,
            aspectRatio: 2,
            scales: {
                x: {
                    title: {
                        display: true,
                        text: '日期'
                    }
                    ,
                    ticks: {
                        autoSkip: true,
                        maxTicksLimit: 6,
                        maxRotation: 0,
                        minRotation: 0
                    }
                },
                y: {
                    title: {
                        display: true,
                        text: '結婚對數'
                    },
                    beginAtZero: true
                }
            },
            plugins: {
                legend: {
                    position: 'top'
                }
            }
        }
    })

    // 區域分佈圖
    const areaChart = echarts.init(document.getElementById('area-chart'))
    areaChart.setOption({
        tooltip: {
            trigger: 'item',
            formatter: '{b}: {c} 對 ({d}%)'
        },
        legend: {
            orient: 'vertical',
            right: 10,
            type: 'scroll'
        },
        series: [{
            type: 'pie',
            radius: ['40%', '70%'],
            data: [],
            label: {
                show: true,
                formatter: '{b}\n{c} 對'
            }
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
                    'rgba(75, 192, 192, 0.8)',
                    'rgba(54, 162, 235, 0.8)'
                ],
                borderWidth: 1,
                borderColor: '#fff'
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: true,
            plugins: {
                legend: {
                    position: 'bottom',
                    labels: {
                        padding: 20
                    }
                },
                title: {
                    display: true,
                    text: '結婚對數性別比例',
                    font: {
                        size: 16
                    },
                    padding: {
                        top: 10,
                        bottom: 30
                    }
                },
                tooltip: {
                    callbacks: {
                        label: function(context) {
                            const total = context.dataset.data.reduce((a, b) => a + b, 0);
                            const value = context.raw;
                            const percentage = total > 0 ? ((value / total) * 100).toFixed(1) : 0;
                            return `${context.label}: ${value} 對 (${percentage}%)`;
                        }
                    }
                }
            }
        }
    })

    // 國籍分布：直方圖（前 10 名）與分類圓餅（本國籍 / 大陸 / 港澳 / 外國籍）
    const natBarCtx = document.getElementById('nationality-bar').getContext('2d')
    new Chart(natBarCtx, {
        type: 'bar',
        data: {
            labels: [],
            datasets: [
                {
                    label: '同性別',
                    data: [],
                    backgroundColor: 'rgba(75, 192, 192, 0.85)'
                },
                {
                    label: '不同性別',
                    data: [],
                    backgroundColor: 'rgba(54, 162, 235, 0.85)'
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: true,
            aspectRatio: 1.5,
            plugins: {
                title: {
                    display: true,
                    text: '外國籍國別分佈（前 12 名）',
                    font: { size: 16 },
                    padding: { top: 6, bottom: 12 }
                },
                legend: { display: false }
            },
            scales: {
                x: { ticks: { autoSkip: true, maxRotation: 45 }, grid: { display: false } },
                y: { beginAtZero: true }
            }
        }
    })

    const natPieCtx = document.getElementById('nationality-pie').getContext('2d')
    new Chart(natPieCtx, {
        type: 'pie',
        data: {
            labels: ['本國籍', '大陸地區', '港澳地區', '外國籍'],
            datasets: [{
                data: [0, 0, 0, 0],
                backgroundColor: [
                    'rgba(54, 162, 235, 0.8)',
                    'rgba(255, 99, 132, 0.8)',
                    'rgba(255, 205, 86, 0.8)',
                    'rgba(75, 192, 192, 0.8)'
                ],
                borderColor: '#fff',
                borderWidth: 1
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: { position: 'bottom' },
                title: { display: true, text: '國籍分類比例', font: { size: 14 } }
            }
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
    },

    async getChartData() {
        const res = await fetch('/api/chart-data')
        return res.json()
    }
}

// 圖表更新
async function updateCharts() {
    const data = await api.getChartData();
    
    // 更新趨勢圖：使用不會修改原資料的排序（由舊到新）
    const trendChart = Chart.getChart('trend-chart');
    if (trendChart && data.trendData) {
        const points = Array.isArray(data.trendData) ? data.trendData.slice() : [];
        points.sort((a, b) => new Date(a.date) - new Date(b.date));
        // 若點過多，採樣點以避免 x 軸過長（顯示最多 6 個點）
        const maxPoints = 6;
        let displayPoints = points;
        if (points.length > maxPoints) {
            const step = Math.ceil(points.length / maxPoints);
            displayPoints = points.filter((_, idx) => idx % step === 0);
        }
        trendChart.data.labels = displayPoints.map(p => {
            const date = new Date(p.date);
            return `${date.getFullYear()}/${(date.getMonth() + 1).toString().padStart(2, '0')}`;
        });
        trendChart.data.datasets[0].data = displayPoints.map(p => p.total);
        trendChart.update();
    }

    // 更新區域分布圖
    const areaChart = echarts.getInstanceByDom(document.getElementById('area-chart'));
    if (areaChart && data.areaData) {
        areaChart.setOption({
            series: [{
                data: data.areaData.map(d => ({ name: d.name, value: d.value }))
            }]
        });
    }

    // 更新性別比例圖
    const genderChart = Chart.getChart('gender-chart');
    if (genderChart && data.genderData) {
        genderChart.data.datasets[0].data = [
            data.genderData.sameGender,
            data.genderData.differentGender
        ];
        genderChart.update();
    }

    // 更新國籍直方圖與分類圓餅
    const natBar = Chart.getChart('nationality-bar');
    const natPie = Chart.getChart('nationality-pie');

    const nationals = data.nationalityData ? Object.entries(data.nationalityData) : [];
    if (!nationals.length) {
        if (natBar) {
            natBar.data.labels = ['無外國籍資料'];
            natBar.data.datasets[0].data = [0];
            natBar.update();
        }
        if (natPie) {
            natPie.data.datasets[0].data = [0, 0, 0, 0];
            natPie.update();
        }
    } else {
        // 直方圖：前 12 名
        const top = nationals.slice(0, 12);
        if (natBar) {
            // 若後端提供 breakdown，使用同/異兩列；否則把 total 填入不同性別欄位
            if (data.nationalityBreakdown && Object.keys(data.nationalityBreakdown).length) {
                // 過濾掉本國籍並依總和排序取 top 10
                const items = Object.entries(data.nationalityBreakdown)
                    .map(([k, v]) => ({ 
                        country: k, 
                        same: v.same ?? v.Same ?? 0, 
                        diff: v.different ?? v.Different ?? 0 
                    }))
                    .filter(item => !['本國籍', '台灣', '臺灣', '中華民國', 'taiwan', 'taiwanese']
                        .includes(item.country.toLowerCase()));
                items.sort((a,b) => (b.same + b.diff) - (a.same + a.diff));
                const topItems = items.slice(0,10);
                natBar.data.labels = topItems.map(i => i.country);
                natBar.data.datasets[0].data = topItems.map(i => i.same);
                natBar.data.datasets[1].data = topItems.map(i => i.diff);
                natBar.update();
            } else {
                natBar.data.labels = top.map(([country]) => country);
                natBar.data.datasets[0].data = top.map(() => 0);
                natBar.data.datasets[1].data = top.map(([_, count]) => count);
                natBar.update();
            }
        }

        // 圓餅圖：分類為 本國籍 / 大陸 / 港澳 / 外國籍
        const categories = { local: 0, mainland: 0, hk_mo: 0, foreign: 0 };
        const normalize = s => (s || '').toString().toLowerCase();
        for (const [k, v] of nationals) {
            const key = normalize(k);
            const val = Number(v) || 0;
            if (key.includes('台') || key.includes('臺') || key.includes('台灣') || key.includes('中華民國') || key.includes('taiwan')) {
                categories.local += val;
            } else if (key.includes('香港') || key.includes('港')) {
                categories.hk_mo += val;
            } else if (key.includes('澳門') || key.includes('澳')) {
                categories.hk_mo += val;
            } else if (key.includes('中國') || key.includes('大陸') || key.includes('china')) {
                categories.mainland += val;
            } else {
                categories.foreign += val;
            }
        }

        if (natPie) {
            natPie.data.datasets[0].data = [categories.local, categories.mainland, categories.hk_mo, categories.foreign];
            natPie.update();
        }
    }
}

// 初始化
document.addEventListener('DOMContentLoaded', async () => {
    initCharts()
    
    // 初始載入儀表板資料
    const stats = await api.getStats()
    const lu = stats.lastUpdate || stats.last_update || null;
    if (lu) document.getElementById('last-update').textContent = new Date(lu).toLocaleString()
    document.getElementById('total-marriages').textContent = stats.totalMarriages ?? stats.total_marriages ?? 0
    document.getElementById('monthly-change').textContent = `${stats.monthlyChange ?? stats.monthly_change ?? 0}%`

    // 載入並更新所有圖表（updateCharts 會呼叫 API 並更新）
    await updateCharts()
    // 初始化載入圖表資料已完成
})

// 更新抓取歷史
// 抓取歷史功能已移除（Fetch UI 改為後端背景處理）

// 查看詳細資料
function viewDetails(id) {
    // 導向到新的詳細資料頁面
    window.location.href = `/details.html?id=${id}`
}