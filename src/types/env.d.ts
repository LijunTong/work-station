/// <reference types="vite/client" />

import type { WorkStationApi } from './api';

declare global {
  interface Window {
    api: WorkStationApi;
  }
}

export {};
