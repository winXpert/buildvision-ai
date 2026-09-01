import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AreaSelectorComponent } from '../../components/area-selector/area-selector';
import {
  DesignApiService,
  DesignJob,
  SelectionBox
} from '../../services/design-api.service';

@Component({
  selector: 'app-designer-page',
  standalone: true,
  imports: [FormsModule, AreaSelectorComponent],
  templateUrl: './designer-page.html',
  styleUrl: './designer-page.css'
})
export class DesignerPageComponent implements OnInit, OnDestroy {
  private readonly api = inject(DesignApiService);

  projectName = 'Rooftop Terrace';
  prompt = '';
  variations = 2;
  previewUrl = '';
  selectedFile: File | null = null;
  selection: SelectionBox | null = null;

  readonly stylePresets = [
    {
      id: 'minimal',
      name: 'Minimal Modern',
      blurb: 'Clean shade + stone floor + timber accents',
      prompt:
        'Redesign only the right-side rooftop terrace into a Minimal Modern outdoor living space. ' +
        'Add a slim black-metal pergola with light shade fabric, warm grey outdoor stone flooring, ' +
        'a low built-in concrete bench with neutral cushions, sparse tall planters, wooden slat wall paneling on part of the white wall, ' +
        'subtle recessed LED lighting, and keep the existing door fully accessible. ' +
        'Do not change the left railing, staircase, or upper roof structure. Photorealistic, match existing building perspective and lighting.'
    },
    {
      id: 'kerala',
      name: 'Kerala Tropical',
      blurb: 'Pergola, greenery, warm wood, soft evening light',
      prompt:
        'Redesign only the right-side rooftop terrace into a Kerala Tropical outdoor courtyard. ' +
        'Add a timber pergola with climbing greenery and soft filtered shade, terracotta or laterite-toned outdoor flooring, ' +
        'built-in seating with cushions, dense tropical planters (areca, bamboo, palms), a small water bowl fountain against the wall, ' +
        'warm festoon or wall-wash lights, and relocate the outdoor sink into a neat utility niche. ' +
        'Keep the existing door accessible. Do not alter the left railing, stairs, or upper tower. Photorealistic Indian residential architecture.'
    },
    {
      id: 'resort',
      name: 'Luxury Resort',
      blurb: 'Lounge deck, canopy, feature wall, ambient glow',
      prompt:
        'Redesign only the right-side rooftop terrace into a Luxury Resort lounge deck. ' +
        'Add an elegant tensile canopy or pavilion shade, premium wood-look decking, outdoor sofa lounge set with coffee table, ' +
        'a feature wall with textured stone or vertical garden, sculptural planters, a slim water feature, ' +
        'and soft golden ambient lighting for evening mood. Keep circulation to the right-side door clear. ' +
        'Preserve the left balcony railing, staircase, and upper roof pavilion. Photorealistic, high-end residential terrace.'
    }
  ] as const;

  readonly activeStyleId = signal<string | null>(null);
  readonly loading = signal(false);
  readonly aiMode = signal<'unknown' | 'openai' | 'demo'>('unknown');
  readonly error = signal<string | null>(null);
  readonly result = signal<DesignJob | null>(null);
  readonly history = signal<DesignJob[]>([]);
  readonly selectedResultIndex = signal(0);
  readonly comparePosition = signal(50);

  private objectUrl: string | null = null;

  ngOnInit(): void {
    this.api.health().subscribe({
      next: (h) => this.aiMode.set(h.mode === 'openai' ? 'openai' : 'demo'),
      error: () => this.aiMode.set('unknown')
    });
    this.refreshHistory();
  }

  ngOnDestroy(): void {
    if (this.objectUrl) URL.revokeObjectURL(this.objectUrl);
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.selectedFile = file;
    if (this.objectUrl) URL.revokeObjectURL(this.objectUrl);
    this.objectUrl = URL.createObjectURL(file);
    this.previewUrl = this.objectUrl;
    this.result.set(null);
    this.error.set(null);
    this.selection = null;
  }

  onSelectionChange(selection: SelectionBox | null): void {
    this.selection = selection;
  }

  applyStyle(styleId: string): void {
    const style = this.stylePresets.find((s) => s.id === styleId);
    if (!style) return;
    this.activeStyleId.set(style.id);
    this.prompt = style.prompt;
    this.projectName = `Rooftop — ${style.name}`;
  }

  generate(): void {
    if (!this.selectedFile || !this.prompt.trim()) {
      this.error.set('Upload an image and enter a design prompt.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.api
      .generate({
        image: this.selectedFile,
        prompt: this.prompt.trim(),
        projectName: this.projectName.trim() || 'Untitled Project',
        selection: this.selection,
        variations: this.variations
      })
      .subscribe({
        next: (job) => {
          this.result.set(job);
          this.selectedResultIndex.set(0);
          this.comparePosition.set(50);
          this.loading.set(false);
          this.refreshHistory();
        },
        error: (err) => {
          this.loading.set(false);
          this.error.set(err?.error?.error || err?.message || 'Generation failed.');
        }
      });
  }

  selectResult(index: number): void {
    this.selectedResultIndex.set(index);
    this.comparePosition.set(50);
  }

  onCompareInput(event: Event): void {
    const value = Number((event.target as HTMLInputElement).value);
    this.comparePosition.set(value);
  }

  fileUrl(path: string): string {
    return this.api.fileUrl(path);
  }

  activeResultUrl(): string {
    const job = this.result();
    if (!job?.resultImageUrls?.length) return '';
    return this.fileUrl(job.resultImageUrls[this.selectedResultIndex()] || job.resultImageUrls[0]);
  }

  private refreshHistory(): void {
    this.api.list().subscribe({
      next: (jobs) => this.history.set(jobs.slice(0, 8)),
      error: () => undefined
    });
  }

  loadHistoryItem(job: DesignJob): void {
    this.result.set(job);
    this.selectedResultIndex.set(0);
    this.previewUrl = this.fileUrl(job.originalImageUrl);
    this.prompt = job.prompt;
    this.projectName = job.projectName;
  }
}
