import { Configuration, LogLevel } from '@azure/msal-browser';

export const msalConfig: Configuration = {
  auth: {
    clientId: process.env.NEXT_PUBLIC_AAD_CLIENT_ID ?? '',
    authority: `https://login.microsoftonline.com/${process.env.NEXT_PUBLIC_AAD_TENANT_ID ?? 'common'}`,
    redirectUri: typeof window !== 'undefined' ? window.location.origin : '',
  },
  cache: { cacheLocation: 'sessionStorage', storeAuthStateInCookie: false },
  system: {
    loggerOptions: {
      loggerCallback: (level, message, containsPii) => {
        if (containsPii || process.env.NODE_ENV === 'production') return;
        if (level === LogLevel.Error) console.error(message);
      },
    },
  },
};

export const loginRequest = {
  scopes: [`api://${process.env.NEXT_PUBLIC_AAD_CLIENT_ID}/admin.read`],
};

export const apiConfig = {
  baseUrl: process.env.NEXT_PUBLIC_API_BASE_URL ?? 'http://localhost:7071',
  adminApiKey: process.env.NEXT_PUBLIC_ADMIN_API_KEY ?? '',
};
