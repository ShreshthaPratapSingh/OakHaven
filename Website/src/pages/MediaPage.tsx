import { useState, useMemo, useCallback } from 'react';
import { PageIntro } from '@/components/content/PageIntro';
import { ContentSection } from '@/components/content/ContentSection';
import { MediaGrid } from '@/components/media/MediaGrid';
import { MediaLightbox } from '@/components/media/MediaLightbox';
import { cn } from '@/lib/utils';
import {
  mediaOverview,
  mediaCategories,
  mediaItems,
  type MediaCategory,
} from '@/content/media';

export function MediaPage() {
  const [activeCategory, setActiveCategory] = useState<MediaCategory>('all');
  const [lightboxIndex, setLightboxIndex] = useState<number | null>(null);

  const filteredItems = useMemo(() => {
    if (activeCategory === 'all') return mediaItems;
    return mediaItems.filter((item) => item.category === activeCategory);
  }, [activeCategory]);

  const openableItems = useMemo(
    () => filteredItems.filter((item) => item.src.trim().length > 0),
    [filteredItems]
  );

  function handleItemClick(index: number) {
    const item = filteredItems[index];
    if (!item || item.src.trim().length === 0) return;

    const openableIndex = openableItems.findIndex(
      (openable) => openable.id === item.id
    );
    setLightboxIndex(openableIndex >= 0 ? openableIndex : null);
  }

  const handleNext = useCallback(() => {
    setLightboxIndex((index) =>
      index === null ? null : (index + 1) % openableItems.length
    );
  }, [openableItems.length]);

  const handlePrevious = useCallback(() => {
    setLightboxIndex((index) =>
      index === null
        ? null
        : (index - 1 + openableItems.length) % openableItems.length
    );
  }, [openableItems.length]);

  const handleClose = useCallback(() => {
    setLightboxIndex(null);
  }, []);

  return (
    <>
      <PageIntro
        heading={mediaOverview.heading}
        subheading={mediaOverview.subheading}
      />

      <ContentSection className="bg-pine-800/30">
        <div className="mx-auto max-w-7xl px-6 lg:px-8">
          <div className="mb-8 flex flex-wrap gap-2 md:mb-12">
            {mediaCategories.map((category) => (
              <button
                key={category}
                type="button"
                onClick={() => setActiveCategory(category)}
                className={cn(
                  'rounded-sm px-4 py-2 text-sm font-medium tracking-wide transition-colors focus:outline-none focus:ring-2 focus:ring-ember/50',
                  activeCategory === category
                    ? 'bg-ember text-pine-950'
                    : 'bg-white/5 text-mist hover:bg-white/10 hover:text-paper'
                )}
                aria-pressed={activeCategory === category}
              >
                {category.charAt(0).toUpperCase() + category.slice(1)}
              </button>
            ))}
          </div>

          <MediaGrid items={filteredItems} onItemClick={handleItemClick} />
        </div>
      </ContentSection>

      <MediaLightbox
        items={openableItems}
        currentIndex={lightboxIndex ?? 0}
        isOpen={lightboxIndex !== null}
        onClose={handleClose}
        onNext={handleNext}
        onPrevious={handlePrevious}
      />
    </>
  );
}
