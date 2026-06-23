'use client';

import { useState, useEffect, useCallback } from 'react';
import { apiConfig } from '@/lib/msal-config';
import { adminApi } from '@/lib/api-client';

function getToken(): string {
  // MVP: use the static API key from env
  return apiConfig.adminApiKey;
}

export function useAdminData<T>(
  fetcher: (token: string) => Promise<T>
): { data: T | null; loading: boolean; error: string | null; refresh: () => void } {
  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const token = getToken();
      const result = await fetcher(token);
      setData(result);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unknown error');
    } finally {
      setLoading(false);
    }
  }, [fetcher]);

  useEffect(() => { load(); }, [load]);

  return { data, loading, error, refresh: load };
}

export const useOverviewData = () => useAdminData(adminApi.overview);
export const useAiCostsData = () => useAdminData(adminApi.aiCosts);
export const useTenantsData = () => useAdminData(adminApi.tenants);
export const useWhatsAppData = () => useAdminData(adminApi.whatsapp);
export const useBillingData = () => useAdminData(adminApi.billing);
export const useSystemData = () => useAdminData(adminApi.system);
