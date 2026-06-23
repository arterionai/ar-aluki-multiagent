export interface OverviewData {
  messages24h: number;
  messages7d: number;
  messages30d: number;
  activeTenants: number;
  outboundMessages: number;
  generatedAt: string;
}

export interface DayCount {
  day: string;
  count: number;
}

export interface DayTokenCost {
  day: string;
  tokens: number;
  cost: number;
  [key: string]: string | number;
}

export interface FeatureCost {
  feature: string;
  tokens: number;
  cost: number;
  [key: string]: string | number;
}

export interface TenantCost {
  tenantId: string;
  tokens: number;
  cost: number;
  [key: string]: string | number;
}

export interface AiCostsData {
  byDay: DayTokenCost[];
  byFeature: FeatureCost[];
  topTenants: TenantCost[];
  generatedAt: string;
}

export interface TenantRow {
  id: string;
  displayName: string | null;
  createdAt: string;
  messageCount: number;
  lastActivity: string | null;
}

export interface TenantsData {
  tenants: TenantRow[];
  generatedAt: string;
}

export interface WhatsAppData {
  inboundByDay: DayCount[];
  outboundByDay: DayCount[];
  agentDistribution: { agent: string; count: number }[];
  generatedAt: string;
}

export interface StatusCount {
  status: string;
  count: number;
}

export interface StateCount {
  state: string;
  count: number;
}

export interface DayRevenue {
  day: string;
  revenue: number;
}

export interface BillingData {
  accountsByStatus: StatusCount[];
  revenueByDay: DayRevenue[];
  subscriptionsByState: StateCount[];
  totalCreditBalance: number;
  generatedAt: string;
}

export interface TableSize {
  tableName: string;
  size: string;
  sizeBytes: number;
}

export interface SystemData {
  tableSizes: TableSize[];
  failedExtractionJobs24h: number;
  oldestPendingReminderAgeMinutes: number;
  serverUtc: string;
  generatedAt: string;
}
