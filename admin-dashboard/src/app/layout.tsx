import type { Metadata } from 'next';
import './globals.css';
import { MsalProviderWrapper } from '@/components/auth/MsalProviderWrapper';

export const metadata: Metadata = {
  title: 'Aluki Admin Dashboard',
  description: 'Admin dashboard for Aluki Runtime',
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body>
        <MsalProviderWrapper>
          {children}
        </MsalProviderWrapper>
      </body>
    </html>
  );
}
