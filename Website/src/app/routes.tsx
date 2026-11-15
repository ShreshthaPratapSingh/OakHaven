import { createBrowserRouter, RouterProvider } from 'react-router-dom';
import { PageLayout } from '@/components/layout/PageLayout';
import { HomePage } from '@/pages/HomePage';
import { GamePage } from '@/pages/GamePage';
import { GameplayPage } from '@/pages/GameplayPage';
import { WorldPage } from '@/pages/WorldPage';
import { StoryPage } from '@/pages/StoryPage';
import { CharactersPage } from '@/pages/CharactersPage';
import { MediaPage } from '@/pages/MediaPage';
import { DevelopmentPage } from '@/pages/DevelopmentPage';
import { NotFoundPage } from '@/pages/NotFoundPage';

const router = createBrowserRouter([
  {
    path: '/',
    element: (
      <PageLayout>
        <HomePage />
      </PageLayout>
    ),
  },
  {
    path: '/game',
    element: (
      <PageLayout>
        <GamePage />
      </PageLayout>
    ),
  },
  {
    path: '/gameplay',
    element: (
      <PageLayout>
        <GameplayPage />
      </PageLayout>
    ),
  },
  {
    path: '/world',
    element: (
      <PageLayout>
        <WorldPage />
      </PageLayout>
    ),
  },
  {
    path: '/story',
    element: (
      <PageLayout>
        <StoryPage />
      </PageLayout>
    ),
  },
  {
    path: '/characters',
    element: (
      <PageLayout>
        <CharactersPage />
      </PageLayout>
    ),
  },
  {
    path: '/media',
    element: (
      <PageLayout>
        <MediaPage />
      </PageLayout>
    ),
  },
  {
    path: '/development',
    element: (
      <PageLayout>
        <DevelopmentPage />
      </PageLayout>
    ),
  },
  {
    path: '*',
    element: (
      <PageLayout>
        <NotFoundPage />
      </PageLayout>
    ),
  },
]);

export function AppRouter() {
  return <RouterProvider router={router} />;
}
