'use client';

import { MsalProvider } from '@azure/msal-react';
import { PublicClientApplication } from '@azure/msal-browser';
import { msalConfig } from '@/lib/msal-config';

// Created at module level so it's not re-instantiated on re-renders.
// MSAL v4 begins async initialization in the constructor; MsalProvider
// waits for it before rendering children.
const msalInstance = new PublicClientApplication(msalConfig);

export function MsalProviderWrapper({ children }: { children: React.ReactNode }) {
  return <MsalProvider instance={msalInstance}>{children}</MsalProvider>;
}
