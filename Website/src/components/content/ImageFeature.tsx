import { cn } from '@/lib/utils';
import { PlaceholderImage } from '@/components/media/PlaceholderImage';
import { ResponsiveImage } from '@/components/media/ResponsiveImage';
import { FadeIn } from './FadeIn';

interface ImageFeatureProps {
  title: string;
  description: string;
  imageLabel: string;
  imageAlt: string;
  src?: string;
  width?: number;
  height?: number;
  srcSet?: string;
  sizes?: string;
  reversed?: boolean;
}

export function ImageFeature({
  title,
  description,
  imageLabel,
  imageAlt,
  src,
  width,
  height,
  srcSet,
  sizes,
  reversed = false,
}: ImageFeatureProps) {
  return (
    <FadeIn>
      <div className="grid items-center gap-8 lg:grid-cols-2 lg:gap-16">
        <div
          className={cn(
            'space-y-4',
            reversed ? 'lg:order-2 lg:pl-8' : 'lg:pr-8'
          )}
        >
          <h3 className="text-balance text-2xl font-semibold tracking-tight text-paper md:text-3xl">
            {title}
          </h3>
          <p className="text-base leading-relaxed text-mist md:text-lg">
            {description}
          </p>
        </div>

        <div className={cn(reversed ? 'lg:order-1' : '')}>
          {src ? (
            <ResponsiveImage
              src={src}
              alt={imageAlt}
              aspectRatio="video"
              width={width}
              height={height}
              srcSet={srcSet}
              sizes={sizes}
              className="rounded-sm shadow-2xl"
            />
          ) : (
            <PlaceholderImage
              label={imageLabel}
              aspectRatio="video"
              className="rounded-sm shadow-2xl"
            />
          )}
        </div>
      </div>
    </FadeIn>
  );
}
