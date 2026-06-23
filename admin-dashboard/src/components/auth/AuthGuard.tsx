'use client';

import { useIsAuthenticated, useMsal } from '@azure/msal-react';
import { InteractionStatus } from '@azure/msal-browser';
import { loginRequest } from '@/lib/msal-config';

export function AuthGuard({ children }: { children: React.ReactNode }) {
  const { instance, inProgress } = useMsal();
  const isAuthenticated = useIsAuthenticated();
  const msalConfigured = Boolean(process.env.NEXT_PUBLIC_AAD_CLIENT_ID);

  // While MSAL is initializing or processing a redirect, render nothing.
  // Showing the sign-in button here would cause an infinite redirect loop
  // because isAuthenticated is false during handleRedirect.
  if (inProgress !== InteractionStatus.None) {
    return null;
  }

  if (msalConfigured && !isAuthenticated) {
    return (
      <div className="min-h-screen bg-gray-950 flex items-center justify-center">
        <div className="bg-gray-900 rounded-xl p-8 shadow-2xl text-center max-w-sm w-full mx-4">
          <div className="w-16 h-16 bg-blue-600 rounded-xl flex items-center justify-center mx-auto mb-6">
            <svg className="w-8 h-8 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
            </svg>
          </div>
          <h1 className="text-2xl font-bold text-white mb-2">Aluki Admin</h1>
          <p className="text-gray-400 mb-6 text-sm">Inicia sesión con tu cuenta Microsoft para acceder al dashboard.</p>
          <button
            onClick={() => instance.loginRedirect(loginRequest)}
            className="w-full bg-blue-600 hover:bg-blue-700 text-white font-medium py-3 px-4 rounded-lg transition-colors"
          >
            Iniciar sesión con Microsoft
          </button>
        </div>
      </div>
    );
  }

  return <>{children}</>;
}
