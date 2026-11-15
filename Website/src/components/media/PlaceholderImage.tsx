import { cn } from '@/lib/utils';

interface PlaceholderImageProps {
  label?: string;
  className?: string;
  aspectRatio?: 'video' | 'square' | 'portrait' | 'wide';
}

export function PlaceholderImage({
  label,
  className,
  aspectRatio = 'video',
}: PlaceholderImageProps) {
  const aspectClasses = {
    video: 'aspect-video',
    square: 'aspect-square',
    portrait: 'aspect-[3/4]',
    wide: 'aspect-[21/9]',
  };

  return (
    <div
      className={cn(
        'relative flex w-full items-center justify-center overflow-hidden bg-gradient-to-br from-pine-700 via-pine-800 to-pine-950',
        aspectClasses[aspectRatio],
        className
      )}
    >
      <div className="absolute inset-0 bg-[radial-gradient(circle_at_30%_30%,rgba(212,165,116,0.08),transparent_50%)]" />
      <div className="absolute inset-0 opacity-20">
        <svg className="h-full w-full" xmlns="http://www.w3.org/2000/svg">
          <defs>
            <pattern
              id="placeholder-grid"
              width="32"
              height="32"
              patternUnits="userSpaceOnUse"
            >
              <path
                d="M 32 0 L 0 0 0 32"
                fill="none"
                stroke="currentColor"
                strokeWidth="0.5"
              />
            </pattern>
          </defs>
          <rect width="100%" height="100%" fill="url(#placeholder-grid)" />
        </svg>
      </div>
      {label && (
        <span className="relative z-10 px-4 text-center text-xs font-medium uppercase tracking-widest text-mist/70">
          {label}
        </span>
      )}
    </div>
  );
}
