import { useEffect, useRef, useCallback } from 'react';
import { X } from 'lucide-react';
import { NavLink as RouterNavLink } from 'react-router-dom';
import { cn } from '@/lib/utils';
import { useLockBodyScroll } from '@/hooks/useLockBodyScroll';
import type { NavLink } from '@/types';

interface MobileNavigationProps {
  isOpen: boolean;
  onClose: () => void;
  links: NavLink[];
  triggerRef?: React.RefObject<HTMLButtonElement>;
}

function getFocusableElements(container: HTMLElement): HTMLElement[] {
  const selector = [
    'a[href]',
    'button:not([disabled])',
    'input:not([disabled])',
    'select:not([disabled])',
    'textarea:not([disabled])',
    '[tabindex]:not([tabindex="-1"])',
  ].join(', ');

  return Array.from(container.querySelectorAll(selector)).filter(
    (element): element is HTMLElement => element instanceof HTMLElement
  );
}

export function MobileNavigation({
  isOpen,
  onClose,
  links,
  triggerRef,
}: MobileNavigationProps) {
  useLockBodyScroll(isOpen);
  const panelRef = useRef<HTMLDivElement>(null);
  const closeButtonRef = useRef<HTMLButtonElement>(null);

  const handleKeyDown = useCallback(
    (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        onClose();
        return;
      }

      if (event.key !== 'Tab' || !panelRef.current) return;

      const focusable = getFocusableElements(panelRef.current);
      if (focusable.length === 0) return;

      const first = focusable[0];
      const last = focusable[focusable.length - 1];

      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    },
    [onClose]
  );

  useEffect(() => {
    if (isOpen) {
      closeButtonRef.current?.focus();
      document.addEventListener('keydown', handleKeyDown);
    } else {
      triggerRef?.current?.focus();
    }

    return () => {
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [isOpen, handleKeyDown, triggerRef]);

  return (
    <div
      id="mobile-navigation"
      className={cn(
        'fixed inset-0 z-40 md:hidden',
        isOpen ? 'pointer-events-auto' : 'pointer-events-none'
      )}
      aria-hidden={!isOpen}
    >
      <div
        className={cn(
          'absolute inset-0 bg-pine-950/90 backdrop-blur-sm transition-opacity duration-300',
          isOpen ? 'opacity-100' : 'opacity-0'
        )}
        onClick={onClose}
      />

      <div
        ref={panelRef}
        className={cn(
          'absolute right-0 top-0 h-full w-80 max-w-[85vw] transform overflow-y-auto bg-pine-900 shadow-2xl transition-transform duration-300 ease-out',
          isOpen ? 'translate-x-0' : 'translate-x-full'
        )}
      >
        <div className="flex h-20 items-center justify-end px-6">
          <button
            ref={closeButtonRef}
            type="button"
            onClick={onClose}
            className="inline-flex h-11 w-11 items-center justify-center rounded-sm text-paper transition-colors hover:bg-white/10 focus:outline-none focus:ring-2 focus:ring-ember/50"
            aria-label="Close menu"
          >
            <X className="h-6 w-6" aria-hidden="true" />
          </button>
        </div>

        <nav aria-label="Mobile primary">
          <ul className="flex flex-col gap-2 px-6 pb-8">
            {links.map((link) => (
              <li key={link.href}>
                <RouterNavLink
                  to={link.href}
                  onClick={onClose}
                  className={({ isActive }) =>
                    cn(
                      'block rounded-sm px-4 py-4 text-lg font-medium tracking-wide transition-colors hover:bg-white/5',
                      isActive ? 'text-paper' : 'text-mist'
                    )
                  }
                >
                  {link.label}
                </RouterNavLink>
              </li>
            ))}
          </ul>
        </nav>
      </div>
    </div>
  );
}
