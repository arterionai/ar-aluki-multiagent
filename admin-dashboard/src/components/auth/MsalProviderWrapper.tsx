'use client';

import { MsalProvider } from '@azure/msal-react';
import { PublicClientApplication } from '@azure/msal-browser';
import { msalConfig } from '@/lib/msal-config';
import { useMemo } from 'react';

export function MsalProviderWrapper({ children }: { children: React.ReactNode }) {
  const msalInstance = useMemo(() => new PublicClientApplication(msalConfig), []);
  return <MsalProvider instance={msalInstance}>{children}</MsalProvider>;
}
