import { cn } from '@/lib/utils';
import type { NavLink } from '@/types';
import { NavLink as RouterNavLink } from 'react-router-dom';

interface MainNavigationProps {
  links: NavLink[];
}

export function MainNavigation({ links }: MainNavigationProps) {
  return (
    <nav aria-label="Primary" className="hidden md:block">
      <ul className="flex items-center gap-4 lg:gap-8">
        {links.map((link) => (
          <li key={link.href}>
            <RouterNavLink
              to={link.href}
              className={({ isActive }) =>
                cn(
                  'relative py-2 text-xs font-medium tracking-wide transition-colors hover:text-paper lg:text-sm',
                  isActive ? 'text-paper' : 'text-mist'
                )
              }
            >
              {({ isActive }) => (
                <>
                  {link.label}
                  {isActive && (
                    <span className="absolute -bottom-0.5 left-0 h-px w-full bg-ember" />
                  )}
                </>
              )}
            </RouterNavLink>
          </li>
        ))}
      </ul>
    </nav>
  );
}
