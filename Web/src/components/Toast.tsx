import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from 'react';

type ToastTone = 'success' | 'info' | 'error';

interface ToastItem {
  id: number;
  tone: ToastTone;
  title: string;
  body?: string;
}

type ShowToast = (toast: Omit<ToastItem, 'id'>) => void;

const ToastContext = createContext<ShowToast | null>(null);

const TONE_CLASS: Record<ToastTone, string> = {
  success: 'border-ok/60',
  info: 'border-divider',
  error: 'border-danger/70',
};

export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<ToastItem[]>([]);
  const nextId = useRef(1);

  const dismiss = useCallback((id: number) => setItems((list) => list.filter((t) => t.id !== id)), []);

  const show = useCallback<ShowToast>((toast) => {
    const id = nextId.current++;
    setItems((list) => [...list, { ...toast, id }]);
  }, []);

  return (
    <ToastContext.Provider value={show}>
      {children}
      <div
        role="status"
        aria-live="polite"
        className="pointer-events-none fixed inset-x-4 bottom-4 z-50 flex flex-col items-center gap-2 sm:inset-x-auto sm:right-6 sm:bottom-6 sm:items-end"
      >
        {items.map((item) => (
          <ToastCard key={item.id} item={item} onDismiss={dismiss} />
        ))}
      </div>
    </ToastContext.Provider>
  );
}

function ToastCard({ item, onDismiss }: { item: ToastItem; onDismiss: (id: number) => void }) {
  useEffect(() => {
    const timer = setTimeout(() => onDismiss(item.id), 7000);
    return () => clearTimeout(timer);
  }, [item.id, onDismiss]);

  return (
    <div
      className={`pointer-events-auto w-full max-w-sm rounded-xl border bg-panel px-4 py-3 shadow-xl shadow-black/40 ${TONE_CLASS[item.tone]}`}
    >
      <div className="flex items-start gap-3">
        <div className="min-w-0 flex-1">
          <p className="font-semibold">{item.title}</p>
          {item.body && <p className="mt-0.5 text-sm text-muted">{item.body}</p>}
        </div>
        <button
          type="button"
          onClick={() => onDismiss(item.id)}
          className="-m-2 inline-flex size-11 shrink-0 cursor-pointer items-center justify-center rounded-lg text-muted transition-colors duration-200 hover:text-fg"
          aria-label="알림 닫기"
        >
          <svg viewBox="0 0 24 24" className="size-4" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
            <path d="M6 6l12 12M18 6L6 18" strokeLinecap="round" />
          </svg>
        </button>
      </div>
    </div>
  );
}

export function useToast(): ShowToast {
  const show = useContext(ToastContext);
  if (!show) throw new Error('ToastProvider가 없습니다.');
  return show;
}
