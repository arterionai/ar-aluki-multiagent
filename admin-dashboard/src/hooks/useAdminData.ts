'use client';

import { useState, useEffect, useCallback } from 'react';
import { useMsal } from '@azure/msal-react';
import { loginRequest, apiConfig } from '@/lib/msal-config';
import type {
  OverviewData,
  AiCostsData,
  TenantsData,
  WhatsAppData,
  BillingData,
  SystemData,
} from '@/types/admin';

export function useAdminData<T>(
  path: string
): { data: T | null; loading: boolean; error: string | null; refresh: () => void } {
  const { instance, accounts } = useMsal();
  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    const account = accounts[0];
    if (!account) {
      // AuthGuard handles unauthenticated state; nothing to load yet.
      setLoading(false);
      return;
    }

    setLoading(true);
    setError(null);

    let accessToken: string;
    try {
      const result = await instance.acquireTokenSilent({ ...loginRequest, account });
      accessToken = result.accessToken;
    } catch {
      try {
        await instance.acquireTokenRedirect({ ...loginRequest, account });
      } catch {
        setError('No se pudo obtener el token de autenticación');
        setLoading(false);
      }
      return;
    }

    try {
      const res = await fetch(`${apiConfig.baseUrl}/api/dashboard/${path}`, {
        headers: { Authorization: `Bearer ${accessToken}` },
      });
      if (!res.ok) throw new Error(`Admin API error: ${res.status} ${res.statusText}`);
      setData(await res.json() as T);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unknown error');
    } finally {
      setLoading(false);
    }
  }, [path, instance, accounts]);

  useEffect(() => { load(); }, [load]);

  return { data, loading, error, refresh: load };
}

export const useOverviewData = () => useAdminData<OverviewData>('overview');
export const useAiCostsData = () => useAdminData<AiCostsData>('ai-costs');
export const useTenantsData = () => useAdminData<TenantsData>('tenants');
export const useWhatsAppData = () => useAdminData<WhatsAppData>('whatsapp');
export const useBillingData = () => useAdminData<BillingData>('billing');
export const useSystemData = () => useAdminData<SystemData>('system');
