import { cn } from '@/lib/utils';
import type { ReactNode } from 'react';

interface ContentSectionProps {
  children: ReactNode;
  className?: string;
  id?: string;
}

export function ContentSection({
  children,
  className,
  id,
}: ContentSectionProps) {
  return (
    <section
      id={id}
      className={cn('py-16 md:py-24 lg:py-32', className)}
    >
      {children}
    </section>
  );
}
