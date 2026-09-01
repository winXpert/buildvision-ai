import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AreaSelectorComponent } from '../../components/area-selector/area-selector';
import {
  DesignApiService,
  DesignJob,
  DesignSuggestionOption,
  DesignSuggestionResponse,
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
  question = 'What can we make/design in this selected area so that it looks beautiful?';
  variations = 2;
  previewUrl = '';
  selectedFile: File | null = null;
  selection: SelectionBox | null = null;

  readonly loadingSuggest = signal(false);
  readonly loadingGenerate = signal(false);
  readonly aiMode = signal<'unknown' | 'openai' | 'demo'>('unknown');
  readonly error = signal<string | null>(null);
  readonly suggestions = signal<DesignSuggestionResponse | null>(null);
  readonly selectedOptionId = signal<string | null>(null);
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
    this.resetAnalysis();
  }

  onSelectionChange(selection: SelectionBox | null): void {
    this.selection = selection;
    // New region → clear prior options so user re-asks for this area
    this.suggestions.set(null);
    this.selectedOptionId.set(null);
    this.result.set(null);
    this.error.set(null);
  }

  selectedOption(): DesignSuggestionOption | null {
    const id = this.selectedOptionId();
    const list = this.suggestions()?.options ?? [];
    return list.find((o) => o.id === id) ?? null;
  }

  askSuggestions(): void {
    if (!this.selectedFile) {
      this.error.set('Upload a construction image first.');
      return;
    }
    if (!this.selection) {
      this.error.set('Drag on the image to select the area you want to redesign.');
      return;
    }

    this.loadingSuggest.set(true);
    this.error.set(null);
    this.result.set(null);
    this.selectedOptionId.set(null);

    this.api
      .suggest({
        image: this.selectedFile,
        question: this.question.trim() || 'What can we design in this selected area so that it looks beautiful?',
        selection: this.selection
      })
      .subscribe({
        next: (res) => {
          this.suggestions.set(res);
          if (res.options.length) {
            this.selectedOptionId.set(res.options[0].id);
          }
          this.loadingSuggest.set(false);
        },
        error: (err) => {
          this.loadingSuggest.set(false);
          this.error.set(err?.error?.error || err?.message || 'Could not analyze the selected area.');
        }
      });
  }

  chooseOption(option: DesignSuggestionOption): void {
    this.selectedOptionId.set(option.id);
  }

  generate(): void {
    const option = this.selectedOption();
    if (!this.selectedFile || !this.selection || !option) {
      this.error.set('Select an area, get suggestions, then choose an option to generate.');
      return;
    }

    this.loadingGenerate.set(true);
    this.error.set(null);

    this.api
      .generate({
        image: this.selectedFile,
        prompt: option.generatePrompt,
        projectName: this.projectName.trim() || option.title,
        selection: this.selection,
        variations: this.variations
      })
      .subscribe({
        next: (job) => {
          this.result.set(job);
          this.selectedResultIndex.set(0);
          this.comparePosition.set(50);
          this.loadingGenerate.set(false);
          this.refreshHistory();
        },
        error: (err) => {
          this.loadingGenerate.set(false);
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
    this.projectName = job.projectName;
  }

  private resetAnalysis(): void {
    this.selection = null;
    this.suggestions.set(null);
    this.selectedOptionId.set(null);
    this.result.set(null);
    this.error.set(null);
  }
}
