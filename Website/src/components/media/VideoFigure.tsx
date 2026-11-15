import { useEffect, useRef } from 'react';
import { cn } from '@/lib/utils';

interface VideoFigureProps {
  src: string;
  poster?: string;
  caption?: string;
  className?: string;
  aspectRatio?: 'video' | 'square' | 'portrait' | 'wide';
  autoPlay?: boolean;
  loop?: boolean;
  muted?: boolean;
  controls?: boolean;
}

export function VideoFigure({
  src,
  poster,
  caption,
  className,
  aspectRatio = 'video',
  autoPlay = false,
  loop = false,
  muted = false,
  controls = true,
}: VideoFigureProps) {
  const videoRef = useRef<HTMLVideoElement>(null);

  useEffect(() => {
    if (autoPlay && videoRef.current) {
      videoRef.current.muted = muted;
      videoRef.current.play().catch(() => {
        // Handled silently if browser restrictions apply
      });
    }
  }, [autoPlay, muted]);

  const aspectClasses = {
    video: 'aspect-video',
    square: 'aspect-square',
    portrait: 'aspect-[3/4]',
    wide: 'aspect-[21/9]',
  };

  return (
    <figure className={cn('relative overflow-hidden rounded-sm bg-pine-950', className)}>
      <div className={cn('relative w-full overflow-hidden', aspectClasses[aspectRatio])}>
        <video
          ref={videoRef}
          controls={controls}
          autoPlay={autoPlay}
          loop={loop}
          muted={muted}
          playsInline
          preload="auto"
          poster={poster || undefined}
          className="h-full w-full object-cover"
        >
          <source src={src} type="video/mp4" />
          Your browser does not support the video tag.
        </video>
      </div>
      {caption && (
        <figcaption className="mt-3 text-center text-xs text-mist">
          {caption}
        </figcaption>
      )}
    </figure>
  );
}
