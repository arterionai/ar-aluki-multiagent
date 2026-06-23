const KEY_API_KEY = 'admin_api_key';
const KEY_API_URL = 'admin_api_url';

export const DEFAULT_API_URL = 'https://func-araluki-dev-6155.azurewebsites.net';

export function getCredentials(): { apiKey: string; apiUrl: string } | null {
  if (typeof window === 'undefined') return null;
  const apiKey = sessionStorage.getItem(KEY_API_KEY);
  const apiUrl = sessionStorage.getItem(KEY_API_URL) ?? DEFAULT_API_URL;
  if (!apiKey) return null;
  return { apiKey, apiUrl };
}

export function saveCredentials(apiKey: string, apiUrl: string): void {
  sessionStorage.setItem(KEY_API_KEY, apiKey);
  sessionStorage.setItem(KEY_API_URL, apiUrl);
}

export function clearCredentials(): void {
  sessionStorage.removeItem(KEY_API_KEY);
  sessionStorage.removeItem(KEY_API_URL);
}
