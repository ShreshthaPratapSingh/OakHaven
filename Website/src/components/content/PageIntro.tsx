import { ContentSection } from './ContentSection';

interface PageIntroProps {
  heading: string;
  subheading?: string;
}

export function PageIntro({ heading, subheading }: PageIntroProps) {
  return (
    <ContentSection className="pt-32 md:pt-40">
      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <h1 className="text-balance text-4xl font-semibold tracking-tight text-paper md:text-5xl lg:text-6xl">
          {heading}
        </h1>
        {subheading && (
          <p className="mt-6 max-w-2xl text-lg leading-relaxed text-mist">
            {subheading}
          </p>
        )}
      </div>
    </ContentSection>
  );
}
