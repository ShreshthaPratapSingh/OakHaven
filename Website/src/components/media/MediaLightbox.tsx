import { useEffect, useRef, useCallback } from 'react';
import { X, ChevronLeft, ChevronRight } from 'lucide-react';
import { useLockBodyScroll } from '@/hooks/useLockBodyScroll';
import type { MediaItem } from '@/types';

interface MediaLightboxProps {
  items: MediaItem[];
  currentIndex: number;
  isOpen: boolean;
  onClose: () => void;
  onNext: () => void;
  onPrevious: () => void;
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

export function MediaLightbox({
  items,
  currentIndex,
  isOpen,
  onClose,
  onNext,
  onPrevious,
}: MediaLightboxProps) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const previousFocusRef = useRef<HTMLElement | null>(null);
  const wasOpenRef = useRef(false);
  useLockBodyScroll(isOpen);

  const currentItem = items[currentIndex];

  const handleKeyDown = useCallback(
    (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        onClose();
        return;
      }

      if (event.key === 'ArrowRight') {
        event.preventDefault();
        onNext();
        return;
      }

      if (event.key === 'ArrowLeft') {
        event.preventDefault();
        onPrevious();
        return;
      }

      if (event.key !== 'Tab' || !dialogRef.current) return;

      const focusable = getFocusableElements(dialogRef.current);
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
    [onClose, onNext, onPrevious]
  );

  useEffect(() => {
    if (isOpen && !wasOpenRef.current) {
      previousFocusRef.current = document.activeElement as HTMLElement;
      closeButtonRef.current?.focus();
    }

    if (!isOpen && wasOpenRef.current) {
      previousFocusRef.current?.focus();
    }

    wasOpenRef.current = isOpen;
  }, [isOpen]);

  useEffect(() => {
    if (!isOpen) return;

    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [isOpen, handleKeyDown]);

  if (!isOpen || !currentItem) return null;

  return (
    <div
      ref={dialogRef}
      role="dialog"
      aria-modal="true"
      aria-label="Media preview"
      className="fixed inset-0 z-50 flex items-center justify-center"
    >
      <div
        className="absolute inset-0 bg-pine-950/95 backdrop-blur-md"
        onClick={onClose}
      />

      <div className="relative z-10 flex h-full w-full flex-col">
        <div className="flex justify-end p-4 md:p-6">
          <button
            ref={closeButtonRef}
            type="button"
            onClick={onClose}
            className="inline-flex h-11 w-11 items-center justify-center rounded-sm text-paper transition-colors hover:bg-white/10 focus:outline-none focus:ring-2 focus:ring-ember/50"
            aria-label="Close preview"
          >
            <X className="h-6 w-6" aria-hidden="true" />
          </button>
        </div>

        <div className="flex flex-1 items-center justify-between px-2 md:px-6">
          <button
            type="button"
            onClick={onPrevious}
            disabled={items.length <= 1}
            className="inline-flex h-11 w-11 shrink-0 items-center justify-center rounded-sm text-paper transition-colors hover:bg-white/10 focus:outline-none focus:ring-2 focus:ring-ember/50 disabled:opacity-30"
            aria-label="Previous image"
          >
            <ChevronLeft className="h-6 w-6" aria-hidden="true" />
          </button>

          <div className="mx-4 max-h-[70vh] max-w-5xl flex-1 md:mx-8">
            {currentItem.type === 'image' ? (
              <img
                src={currentItem.src}
                alt={currentItem.alt}
                className="mx-auto max-h-[70vh] max-w-full rounded-sm object-contain shadow-2xl"
              />
            ) : (
              <video
                src={currentItem.src}
                controls
                className="mx-auto max-h-[70vh] max-w-full rounded-sm shadow-2xl"
                poster=""
              />
            )}
          </div>

          <button
            type="button"
            onClick={onNext}
            disabled={items.length <= 1}
            className="inline-flex h-11 w-11 shrink-0 items-center justify-center rounded-sm text-paper transition-colors hover:bg-white/10 focus:outline-none focus:ring-2 focus:ring-ember/50 disabled:opacity-30"
            aria-label="Next image"
          >
            <ChevronRight className="h-6 w-6" aria-hidden="true" />
          </button>
        </div>

        <div className="p-4 text-center md:p-6">
          <p className="text-sm text-paper md:text-base">
            {currentItem.caption || currentItem.alt}
          </p>
          {currentItem.credit && (
            <p className="mt-1 text-xs text-mist">{currentItem.credit}</p>
          )}
          <p className="mt-2 text-xs text-mist">
            {currentIndex + 1} / {items.length}
          </p>
        </div>
      </div>
    </div>
  );
}
