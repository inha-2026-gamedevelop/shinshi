import { createHttpApi } from './httpClient';
import { createMockApi } from './mockClient';
import type { ApiClient } from './types';

export const isMockMode = import.meta.env.VITE_API_MOCK === '1';

export function createDefaultApi(): ApiClient {
  return isMockMode ? createMockApi() : createHttpApi();
}
