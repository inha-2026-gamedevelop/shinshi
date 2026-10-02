import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { createDefaultApi } from './api';
import { ApiProvider } from './api/ApiContext';
import { AuthProvider } from './auth/AuthContext';
import { ToastProvider } from './components/Toast';
import { App } from './App';
import './index.css';

const api = createDefaultApi();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ApiProvider client={api}>
      <AuthProvider>
        <ToastProvider>
          <BrowserRouter>
            <App />
          </BrowserRouter>
        </ToastProvider>
      </AuthProvider>
    </ApiProvider>
  </StrictMode>,
);
