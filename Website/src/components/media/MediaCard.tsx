import { ResponsiveImage } from './ResponsiveImage';
import { PlaceholderImage } from './PlaceholderImage';
import { FadeIn } from '@/components/content/FadeIn';
import type { MediaItem } from '@/types';

interface MediaCardProps {
  item: MediaItem;
  index: number;
  onClick: () => void;
}

export function MediaCard({ item, index, onClick }: MediaCardProps) {
  const hasSrc = item.src && item.src.trim().length > 0;

  return (
    <FadeIn delay={Math.min(index * 50, 250)}>
      <button
        type="button"
        onClick={onClick}
        disabled={!hasSrc}
        className="group relative w-full overflow-hidden rounded-sm border border-white/10 bg-pine-800/30 text-left transition-colors hover:border-ember/30 focus:outline-none focus:ring-2 focus:ring-ember/50 disabled:cursor-default disabled:hover:border-white/10"
        aria-label={hasSrc ? `Open ${item.alt}` : item.alt}
      >
        {hasSrc ? (
          <ResponsiveImage
            src={item.src}
            alt={item.alt}
            aspectRatio="video"
            width={item.width}
            height={item.height}
            className="transition-transform duration-500 group-hover:scale-105 group-focus:scale-105"
          />
        ) : (
          <PlaceholderImage
            label={`${item.category || 'Media'} — awaiting approved asset`}
            aspectRatio="video"
          />
        )}

        <div className="p-4">
          {item.category && (
            <span className="mb-2 inline-block text-xs font-medium uppercase tracking-wider text-ember">
              {item.category}
            </span>
          )}
          <p className="text-sm text-paper">{item.caption || item.alt}</p>
          {item.credit && (
            <p className="mt-1 text-xs text-mist">{item.credit}</p>
          )}
        </div>
      </button>
    </FadeIn>
  );
}
