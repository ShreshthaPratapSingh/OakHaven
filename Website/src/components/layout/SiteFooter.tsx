import { Link } from 'react-router-dom';
import type { NavLink } from '@/types';

interface SiteFooterProps {
  links: NavLink[];
}

export function SiteFooter({ links }: SiteFooterProps) {
  const currentYear = new Date().getFullYear();

  return (
    <footer className="border-t border-white/10 bg-pine-950 py-12">
      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="flex flex-col items-start justify-between gap-8 md:flex-row md:items-center">
          <div>
            <Link
              to="/"
              className="font-display text-xl font-bold tracking-wider text-paper focus:outline-none focus:ring-2 focus:ring-ember/50 focus:ring-offset-2 focus:ring-offset-pine-950"
            >
              OakHaven
            </Link>
            <p className="mt-2 max-w-sm text-sm text-mist">
              An official game website. Development updates and media will be
              shared as they are ready.
            </p>
          </div>

          <nav aria-label="Footer">
            <ul className="flex flex-wrap gap-6">
              {links.map((link) => (
                <li key={link.href}>
                  <Link
                    to={link.href}
                    className="text-sm text-mist transition-colors hover:text-paper focus:outline-none focus:ring-2 focus:ring-ember/50"
                  >
                    {link.label}
                  </Link>
                </li>
              ))}
            </ul>
          </nav>
        </div>

        <div className="mt-10 border-t border-white/10 pt-6">
          <p className="text-xs text-mist/60">
            © {currentYear} OakHaven. All rights reserved.
          </p>
        </div>
      </div>
    </footer>
  );
}
