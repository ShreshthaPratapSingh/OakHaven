import { SiteHeader } from './SiteHeader';
import { SiteFooter } from './SiteFooter';
import { navLinks } from '@/content/site';
import type { ReactNode } from 'react';

interface PageLayoutProps {
  children: ReactNode;
}

export function PageLayout({ children }: PageLayoutProps) {
  return (
    <div className="flex min-h-screen flex-col">
      <a
        href="#main-content"
        className="fixed left-4 top-4 z-50 -translate-y-24 rounded-sm bg-ember px-4 py-2 text-sm font-medium text-pine-950 transition-transform focus:translate-y-0 focus:outline-none focus:ring-2 focus:ring-ember/50"
      >
        Skip to main content
      </a>

      <SiteHeader links={navLinks} />

      <main id="main-content" className="flex-1">
        {children}
      </main>

      <SiteFooter links={navLinks} />
    </div>
  );
}
