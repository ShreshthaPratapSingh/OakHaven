import { cn } from '@/lib/utils';

interface ResponsiveImageProps {
  src: string;
  alt: string;
  className?: string;
  aspectRatio?: 'video' | 'square' | 'portrait' | 'wide';
  loading?: 'lazy' | 'eager';
  width?: number;
  height?: number;
  srcSet?: string;
  sizes?: string;
}

export function ResponsiveImage({
  src,
  alt,
  className,
  aspectRatio = 'video',
  loading = 'lazy',
  width,
  height,
  srcSet,
  sizes,
}: ResponsiveImageProps) {
  const aspectClasses = {
    video: 'aspect-video',
    square: 'aspect-square',
    portrait: 'aspect-[3/4]',
    wide: 'aspect-[21/9]',
  };

  return (
    <div
      className={cn(
        'relative overflow-hidden bg-pine-800',
        aspectClasses[aspectRatio],
        className
      )}
    >
      <img
        src={src}
        alt={alt}
        loading={loading}
        decoding="async"
        width={width}
        height={height}
        srcSet={srcSet}
        sizes={sizes}
        className="h-full w-full object-cover transition-transform duration-500"
      />
    </div>
  );
}
