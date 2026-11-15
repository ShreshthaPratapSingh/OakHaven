import { MediaCard } from './MediaCard';
import type { MediaItem } from '@/types';

interface MediaGridProps {
  items: MediaItem[];
  onItemClick: (index: number) => void;
}

export function MediaGrid({ items, onItemClick }: MediaGridProps) {
  if (items.length === 0) {
    return (
      <p className="text-center text-mist">
        No media has been published yet. Check back later for screenshots,
        artwork, and videos.
      </p>
    );
  }

  return (
    <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
      {items.map((item, index) => (
        <MediaCard
          key={item.id}
          item={item}
          index={index}
          onClick={() => onItemClick(index)}
        />
      ))}
    </div>
  );
}
