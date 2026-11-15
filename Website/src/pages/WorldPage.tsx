import { PageIntro } from '@/components/content/PageIntro';
import { ContentSection } from '@/components/content/ContentSection';
import { ImageFeature } from '@/components/content/ImageFeature';
import { worldOverview, environmentEntries } from '@/content/world';

export function WorldPage() {
  return (
    <>
      <PageIntro
        heading={worldOverview.heading}
        subheading={worldOverview.subheading}
      />

      <ContentSection className="bg-pine-800/30">
        <div className="mx-auto max-w-7xl px-6 lg:px-8">
          <div className="flex flex-col gap-16 md:gap-24 lg:gap-32">
            {environmentEntries.map((entry, index) => (
              <ImageFeature
                key={entry.id}
                title={entry.title}
                description={entry.description}
                imageLabel={entry.imageLabel}
                imageAlt={entry.imageAlt}
                src={entry.src}
                reversed={index % 2 === 1}
              />
            ))}
          </div>
        </div>
      </ContentSection>
    </>
  );
}
