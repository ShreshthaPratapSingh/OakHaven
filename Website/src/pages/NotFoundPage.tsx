import { Link } from 'react-router-dom';
import { ContentSection } from '@/components/content/ContentSection';
import { Button } from '@/components/ui/Button';

export function NotFoundPage() {
  return (
    <ContentSection className="flex flex-1 items-center justify-center pt-32 md:pt-40">
      <div className="mx-auto max-w-2xl px-6 text-center lg:px-8">
        <h1 className="font-display text-6xl font-bold text-paper md:text-8xl">
          404
        </h1>
        <p className="mt-6 text-lg text-mist">
          This page could not be found. It may have moved or does not exist yet.
        </p>
        <div className="mt-8">
          <Button asChild>
            <Link to="/">Return Home</Link>
          </Button>
        </div>
      </div>
    </ContentSection>
  );
}
