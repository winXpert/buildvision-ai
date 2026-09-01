import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface SelectionBox {
  x: number;
  y: number;
  width: number;
  height: number;
  imageWidth: number;
  imageHeight: number;
}

export interface DesignJob {
  id: string;
  projectName: string;
  prompt: string;
  originalImageUrl: string;
  resultImageUrls: string[];
  status: string;
  error?: string | null;
  usedDemoMode: boolean;
  createdAt: string;
}

export interface HealthStatus {
  status: string;
  product: string;
  aiConfigured: boolean;
  mode: 'openai' | 'demo' | string;
}

@Injectable({ providedIn: 'root' })
export class DesignApiService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  health(): Observable<HealthStatus> {
    return this.http.get<HealthStatus>(`${this.base}/api/health`);
  }

  list(): Observable<DesignJob[]> {
    return this.http.get<DesignJob[]>(`${this.base}/api/designs`);
  }

  get(id: string): Observable<DesignJob> {
    return this.http.get<DesignJob>(`${this.base}/api/designs/${id}`);
  }

  generate(payload: {
    image: File;
    prompt: string;
    projectName: string;
    selection: SelectionBox | null;
    variations: number;
  }): Observable<DesignJob> {
    const form = new FormData();
    form.append('image', payload.image);
    form.append('prompt', payload.prompt);
    form.append('projectName', payload.projectName);
    form.append('variations', String(payload.variations));
    if (payload.selection) {
      form.append('selectionJson', JSON.stringify(payload.selection));
    }
    return this.http.post<DesignJob>(`${this.base}/api/designs/generate`, form);
  }

  fileUrl(path: string): string {
    if (!path) return '';
    if (path.startsWith('http')) return path;
    return `${this.base}${path}`;
  }
}
