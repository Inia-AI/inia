// Mock Data Generator for Marketing Analytics Demo

export interface AdPlatformData {
  date: string
  campaign: string
  adGroup?: string
  impressions: number
  clicks: number
  cost: number
  conversions: number
  revenue?: number
}

// Generate Google Ads CSV data
export const generateGoogleAdsCSV = (scenario: 'monthly' | 'campaign' | 'historical' | 'roi' = 'monthly'): string => {
  const headers = ['Date', 'Campaign', 'Ad group', 'Impressions', 'Clicks', 'Cost', 'Conversions', 'Conv. value']
  const rows: string[][] = []
  
  if (scenario === 'monthly') {
    // Last 30 days of data
    const campaigns = ['Brand Campaign', 'Product Launch', 'Retargeting', 'Discovery']
    const today = new Date()
    
    for (let i = 29; i >= 0; i--) {
      const date = new Date(today)
      date.setDate(date.getDate() - i)
      const dateStr = date.toISOString().split('T')[0] ?? ''
      
      campaigns.forEach(campaign => {
        const baseMetrics = getBaseMetrics(campaign)
        const dayVariation = Math.random() * 0.3 + 0.85 // 85-115% variation
        
        rows.push([
          dateStr,
          campaign,
          `${campaign} - Ad Group 1`,
          Math.round(baseMetrics.impressions * dayVariation).toString(),
          Math.round(baseMetrics.clicks * dayVariation).toString(),
          (baseMetrics.cost * dayVariation).toFixed(2),
          Math.round(baseMetrics.conversions * dayVariation).toString(),
          (baseMetrics.revenue * dayVariation).toFixed(2)
        ])
      })
    }
  } else if (scenario === 'campaign') {
    // Multiple campaigns for comparison
    const campaigns = [
      { name: 'Q4 Holiday Sale', performance: 1.4 },
      { name: 'Black Friday Special', performance: 1.8 },
      { name: 'Year End Clearance', performance: 1.2 },
      { name: 'New Year Campaign', performance: 1.0 },
      { name: 'Winter Collection', performance: 0.9 }
    ]
    
    const startDate = new Date('2024-11-01')
    
    campaigns.forEach(campaign => {
      for (let i = 0; i < 30; i++) {
        const date = new Date(startDate)
        date.setDate(date.getDate() + i)
        const dateStr = date.toISOString().split('T')[0] ?? ''
        
        const baseMetrics = getBaseMetrics('standard')
        rows.push([
          dateStr,
          campaign.name,
          `${campaign.name} - Primary`,
          Math.round(baseMetrics.impressions * campaign.performance).toString(),
          Math.round(baseMetrics.clicks * campaign.performance).toString(),
          (baseMetrics.cost * campaign.performance).toFixed(2),
          Math.round(baseMetrics.conversions * campaign.performance).toString(),
          (baseMetrics.revenue * campaign.performance * 1.2).toFixed(2)
        ])
      }
    })
  } else if (scenario === 'historical') {
    // 6 months of historical data for trend analysis
    const startDate = new Date('2024-06-01')
    const campaigns = ['Ongoing Campaign', 'Seasonal Campaign']
    
    for (let i = 0; i < 180; i++) {
      const date = new Date(startDate)
      date.setDate(date.getDate() + i)
      const dateStr = date.toISOString().split('T')[0] ?? ''
      
      // Add growth trend over time
      const growthFactor = 1 + (i / 180) * 0.5 // 50% growth over 6 months
      const seasonalFactor = 1 + Math.sin(i / 30) * 0.3 // Seasonal variation
      
      campaigns.forEach(campaign => {
        const baseMetrics = getBaseMetrics(campaign)
        rows.push([
          dateStr,
          campaign,
          `${campaign} - Main`,
          Math.round(baseMetrics.impressions * growthFactor * seasonalFactor).toString(),
          Math.round(baseMetrics.clicks * growthFactor * seasonalFactor).toString(),
          (baseMetrics.cost * growthFactor).toFixed(2),
          Math.round(baseMetrics.conversions * growthFactor * seasonalFactor).toString(),
          (baseMetrics.revenue * growthFactor * seasonalFactor).toFixed(2)
        ])
      })
    }
  } else if (scenario === 'roi') {
    // ROI focused data with clear winners and losers
    const campaigns = [
      { name: 'High ROI Campaign', roi: 3.5, scale: 0.8 },
      { name: 'Medium ROI Campaign', roi: 1.8, scale: 1.2 },
      { name: 'Low ROI Campaign', roi: 0.7, scale: 1.5 },
      { name: 'Break Even Campaign', roi: 1.0, scale: 1.0 },
      { name: 'Experimental Campaign', roi: 2.2, scale: 0.5 }
    ]
    
    const today = new Date()
    
    campaigns.forEach(campaign => {
      for (let i = 14; i >= 0; i--) {
        const date = new Date(today)
        date.setDate(date.getDate() - i)
        const dateStr = date.toISOString().split('T')[0] ?? ''
        
        const cost = 500 * campaign.scale * (Math.random() * 0.3 + 0.85)
        const revenue = cost * campaign.roi
        const conversions = Math.round(revenue / 45) // Avg order value $45
        const cpc = 2.5 * (2 - campaign.roi / 2) // Better ROI = lower CPC
        const clicks = Math.round(cost / cpc)
        const impressions = Math.round(clicks * 100 / (3 + campaign.roi)) // CTR based on ROI
        
        rows.push([
          dateStr,
          campaign.name,
          `${campaign.name} - Target`,
          impressions.toString(),
          clicks.toString(),
          cost.toFixed(2),
          conversions.toString(),
          revenue.toFixed(2)
        ])
      }
    })
  }
  
  // Convert to CSV string
  const csvContent = [headers.join(','), ...rows.map(row => row.join(','))].join('\n')
  return csvContent
}

// Generate Facebook Ads CSV data
export const generateFacebookAdsCSV = (scenario: 'monthly' | 'campaign' | 'historical' | 'roi' = 'monthly'): string => {
  // Facebook uses different column names
  const headers = ['reporting_starts', 'campaign_name', 'adset_name', 'reach', 'impressions', 'clicks', 'spend', 'purchases', 'purchase_value']
  const rows: string[][] = []
  
  if (scenario === 'monthly') {
    const campaigns = ['Awareness Campaign', 'Consideration Campaign', 'Conversion Campaign', 'Retention Campaign']
    const today = new Date()
    
    for (let i = 29; i >= 0; i--) {
      const date = new Date(today)
      date.setDate(date.getDate() - i)
      const dateStr = date.toISOString().split('T')[0] ?? ''
      
      campaigns.forEach(campaign => {
        const baseMetrics = getBaseMetrics(campaign)
        const dayVariation = Math.random() * 0.4 + 0.8 // 80-120% variation
        
        rows.push([
          dateStr,
          campaign,
          `${campaign} - Lookalike`,
          Math.round(baseMetrics.impressions * 0.7 * dayVariation).toString(), // Reach is ~70% of impressions
          Math.round(baseMetrics.impressions * dayVariation).toString(),
          Math.round(baseMetrics.clicks * dayVariation).toString(),
          (baseMetrics.cost * dayVariation).toFixed(2),
          Math.round(baseMetrics.conversions * dayVariation).toString(),
          (baseMetrics.revenue * dayVariation).toFixed(2)
        ])
      })
    }
  } else if (scenario === 'campaign') {
    const campaigns = [
      { name: 'Video Ad Campaign', performance: 1.3 },
      { name: 'Carousel Ad Campaign', performance: 1.1 },
      { name: 'Collection Ad Campaign', performance: 1.5 },
      { name: 'Dynamic Product Ads', performance: 1.7 },
      { name: 'Lead Generation Campaign', performance: 0.8 }
    ]
    
    const startDate = new Date('2024-11-01')
    
    campaigns.forEach(campaign => {
      for (let i = 0; i < 30; i++) {
        const date = new Date(startDate)
        date.setDate(date.getDate() + i)
        const dateStr = date.toISOString().split('T')[0] ?? ''
        
        const baseMetrics = getBaseMetrics('standard')
        const reach = Math.round(baseMetrics.impressions * 0.65 * campaign.performance)
        
        rows.push([
          dateStr,
          campaign.name,
          `${campaign.name} - Interest Based`,
          reach.toString(),
          Math.round(baseMetrics.impressions * campaign.performance).toString(),
          Math.round(baseMetrics.clicks * campaign.performance).toString(),
          (baseMetrics.cost * campaign.performance).toFixed(2),
          Math.round(baseMetrics.conversions * campaign.performance).toString(),
          (baseMetrics.revenue * campaign.performance * 1.15).toFixed(2)
        ])
      }
    })
  } else if (scenario === 'historical') {
    const startDate = new Date('2024-06-01')
    const campaigns = ['Brand Awareness', 'Product Sales']
    
    for (let i = 0; i < 180; i++) {
      const date = new Date(startDate)
      date.setDate(date.getDate() + i)
      const dateStr = date.toISOString().split('T')[0] ?? ''
      
      const growthFactor = 1 + (i / 180) * 0.6 // 60% growth
      const seasonalFactor = 1 + Math.cos(i / 30) * 0.25
      
      campaigns.forEach(campaign => {
        const baseMetrics = getBaseMetrics(campaign)
        const reach = Math.round(baseMetrics.impressions * 0.6 * growthFactor * seasonalFactor)
        
        rows.push([
          dateStr,
          campaign,
          `${campaign} - Broad`,
          reach.toString(),
          Math.round(baseMetrics.impressions * growthFactor * seasonalFactor).toString(),
          Math.round(baseMetrics.clicks * growthFactor * seasonalFactor).toString(),
          (baseMetrics.cost * growthFactor).toFixed(2),
          Math.round(baseMetrics.conversions * growthFactor * seasonalFactor).toString(),
          (baseMetrics.revenue * growthFactor * seasonalFactor).toFixed(2)
        ])
      })
    }
  } else if (scenario === 'roi') {
    const campaigns = [
      { name: 'Retargeting Campaign', roi: 4.2, scale: 0.6 },
      { name: 'Lookalike Audience', roi: 2.5, scale: 1.0 },
      { name: 'Interest Targeting', roi: 1.3, scale: 1.3 },
      { name: 'Broad Audience', roi: 0.9, scale: 1.8 },
      { name: 'Custom Audience', roi: 3.1, scale: 0.7 }
    ]
    
    const today = new Date()
    
    campaigns.forEach(campaign => {
      for (let i = 14; i >= 0; i--) {
        const date = new Date(today)
        date.setDate(date.getDate() - i)
        const dateStr = date.toISOString().split('T')[0] ?? ''
        
        const spend = 450 * campaign.scale * (Math.random() * 0.3 + 0.85)
        const purchaseValue = spend * campaign.roi
        const purchases = Math.round(purchaseValue / 50) // Avg order value $50
        const cpc = 3.0 * (2 - campaign.roi / 2)
        const clicks = Math.round(spend / cpc)
        const impressions = Math.round(clicks * 100 / (2.5 + campaign.roi))
        const reach = Math.round(impressions * 0.65)
        
        rows.push([
          dateStr,
          campaign.name,
          `${campaign.name} - Optimized`,
          reach.toString(),
          impressions.toString(),
          clicks.toString(),
          spend.toFixed(2),
          purchases.toString(),
          purchaseValue.toFixed(2)
        ])
      }
    })
  }
  
  const csvContent = [headers.join(','), ...rows.map(row => row.join(','))].join('\n')
  return csvContent
}

// Helper function to get base metrics for consistency
function getBaseMetrics(campaignType: string) {
  const metricsMap: Record<string, any> = {
    'Brand Campaign': { impressions: 50000, clicks: 1500, cost: 750, conversions: 30, revenue: 1350 },
    'Product Launch': { impressions: 80000, clicks: 2800, cost: 1400, conversions: 70, revenue: 3150 },
    'Retargeting': { impressions: 30000, clicks: 1800, cost: 900, conversions: 60, revenue: 2700 },
    'Discovery': { impressions: 60000, clicks: 1200, cost: 600, conversions: 20, revenue: 900 },
    'Awareness Campaign': { impressions: 70000, clicks: 1400, cost: 560, conversions: 14, revenue: 630 },
    'Consideration Campaign': { impressions: 55000, clicks: 1925, cost: 770, conversions: 35, revenue: 1575 },
    'Conversion Campaign': { impressions: 40000, clicks: 2000, cost: 1000, conversions: 50, revenue: 2250 },
    'Retention Campaign': { impressions: 25000, clicks: 1250, cost: 500, conversions: 40, revenue: 1800 },
    'standard': { impressions: 45000, clicks: 1350, cost: 675, conversions: 27, revenue: 1215 },
    'default': { impressions: 40000, clicks: 1200, cost: 600, conversions: 24, revenue: 1080 }
  }
  
  return metricsMap[campaignType] || metricsMap['default']
}

// Generate sample normalized data for charts
export const generateNormalizedData = () => {
  const last30Days: Array<{
    date: string
    platform: string
    impressions: number
    clicks: number
    cost: number
    conversions: number
    revenue: number
    ctr: number
    cpc: number
    roas: number
  }> = []
  const today = new Date()
  
  for (let i = 29; i >= 0; i--) {
    const date = new Date(today)
    date.setDate(date.getDate() - i)
    
    last30Days.push({
      date: date.toISOString().split('T')[0] ?? '',
      platform: 'Google Ads',
      impressions: Math.round(Math.random() * 50000 + 30000),
      clicks: Math.round(Math.random() * 2000 + 1000),
      cost: Math.random() * 1000 + 500,
      conversions: Math.round(Math.random() * 50 + 20),
      revenue: Math.random() * 3000 + 1500,
      ctr: Math.random() * 2 + 1.5,
      cpc: Math.random() * 1 + 1.5,
      roas: Math.random() * 2 + 1.5
    })
    
    last30Days.push({
      date: date.toISOString().split('T')[0] ?? '',
      platform: 'Facebook Ads',
      impressions: Math.round(Math.random() * 60000 + 25000),
      clicks: Math.round(Math.random() * 1800 + 800),
      cost: Math.random() * 900 + 400,
      conversions: Math.round(Math.random() * 40 + 15),
      revenue: Math.random() * 2500 + 1200,
      ctr: Math.random() * 1.5 + 1,
      cpc: Math.random() * 1.5 + 2,
      roas: Math.random() * 1.8 + 1.2
    })
  }
  
  return last30Days
}

// Create a complete mock file object
export const createMockFile = (name: string, content: string) => {
  const lines = content.split('\n')
  const headers = (lines[0] ?? '').split(',').map(h => h.trim())
  const preview = lines.slice(1, 4).map(line => 
    line.split(',').map(cell => cell.trim())
  )
  
  return {
    name,
    size: content.length,
    content,
    headers,
    preview,
    mapping: {}
  }
}