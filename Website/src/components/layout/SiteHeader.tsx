import { useState, useEffect, useRef } from 'react';
import { Menu } from 'lucide-react';
import { Link, useLocation } from 'react-router-dom';
import { cn } from '@/lib/utils';
import { MainNavigation } from './MainNavigation';
import { MobileNavigation } from './MobileNavigation';
import type { NavLink } from '@/types';

interface SiteHeaderProps {
  links: NavLink[];
}

export function SiteHeader({ links }: SiteHeaderProps) {
  const [isScrolled, setIsScrolled] = useState(false);
  const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);
  const mobileMenuTriggerRef = useRef<HTMLButtonElement>(null);
  const location = useLocation();

  useEffect(() => {
    function handleScroll() {
      setIsScrolled(window.scrollY > 24);
    }

    handleScroll();
    window.addEventListener('scroll', handleScroll, { passive: true });
    return () => window.removeEventListener('scroll', handleScroll);
  }, []);

  useEffect(() => {
    setIsMobileMenuOpen(false);
  }, [location]);

  const isHomePage = location.pathname === '/';

  return (
    <>
      <header
        className={cn(
          'fixed left-0 right-0 top-0 z-30 transition-all duration-300',
          isScrolled || !isHomePage
            ? 'bg-pine-900/95 py-4 shadow-lg backdrop-blur-md'
            : 'bg-transparent py-6'
        )}
      >
        <div className="mx-auto flex max-w-7xl items-center justify-between px-6 lg:px-8">
          <Link
            to="/"
            className="font-display text-2xl font-bold tracking-wider text-paper focus:outline-none focus:ring-2 focus:ring-ember/50 focus:ring-offset-2 focus:ring-offset-pine-900"
          >
            OakHaven
          </Link>

          <MainNavigation links={links} />

          <button
            ref={mobileMenuTriggerRef}
            type="button"
            onClick={() => setIsMobileMenuOpen(true)}
            className="inline-flex h-11 w-11 items-center justify-center rounded-sm text-paper transition-colors hover:bg-white/10 focus:outline-none focus:ring-2 focus:ring-ember/50 md:hidden"
            aria-label="Open menu"
            aria-expanded={isMobileMenuOpen}
            aria-controls="mobile-navigation"
          >
            <Menu className="h-6 w-6" aria-hidden="true" />
          </button>
        </div>
      </header>

      <MobileNavigation
        isOpen={isMobileMenuOpen}
        onClose={() => setIsMobileMenuOpen(false)}
        links={links}
        triggerRef={mobileMenuTriggerRef}
      />
    </>
  );
}
