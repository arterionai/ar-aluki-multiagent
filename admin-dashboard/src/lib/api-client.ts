import { apiConfig } from './msal-config';
import type {
  OverviewData,
  AiCostsData,
  TenantsData,
  WhatsAppData,
  BillingData,
  SystemData,
} from '@/types/admin';

async function fetchAdmin<T>(path: string, token: string): Promise<T> {
  const res = await fetch(`${apiConfig.baseUrl}/api/admin/${path}`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!res.ok) throw new Error(`Admin API error: ${res.status} ${res.statusText}`);
  return res.json() as Promise<T>;
}

export const adminApi = {
  overview: (token: string) => fetchAdmin<OverviewData>('overview', token),
  aiCosts: (token: string) => fetchAdmin<AiCostsData>('ai-costs', token),
  tenants: (token: string) => fetchAdmin<TenantsData>('tenants', token),
  whatsapp: (token: string) => fetchAdmin<WhatsAppData>('whatsapp', token),
  billing: (token: string) => fetchAdmin<BillingData>('billing', token),
  system: (token: string) => fetchAdmin<SystemData>('system', token),
};
