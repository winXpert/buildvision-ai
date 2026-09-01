import {
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  ViewChild,
  signal
} from '@angular/core';
import { SelectionBox } from '../../services/design-api.service';

@Component({
  selector: 'app-area-selector',
  standalone: true,
  templateUrl: './area-selector.html',
  styleUrl: './area-selector.css'
})
export class AreaSelectorComponent implements OnChanges {
  @Input({ required: true }) imageUrl = '';
  @Output() selectionChange = new EventEmitter<SelectionBox | null>();

  @ViewChild('canvas', { static: true }) canvasRef!: ElementRef<HTMLCanvasElement>;

  readonly hasImage = signal(false);
  readonly hasSelection = signal(false);

  private image = new Image();
  private drawing = false;
  private activePointerId: number | null = null;
  private startX = 0;
  private startY = 0;
  private current: SelectionBox | null = null;
  private displayScale = 1;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['imageUrl'] && this.imageUrl) {
      this.loadImage(this.imageUrl);
    }
  }

  clearSelection(event?: Event): void {
    event?.preventDefault();
    event?.stopPropagation();
    this.releasePointer();
    this.current = null;
    this.hasSelection.set(false);
    this.selectionChange.emit(null);
    this.redraw();
  }

  onPointerDown(event: PointerEvent): void {
    if (!this.hasImage()) return;
    event.preventDefault();
    const { x, y } = this.toCanvasCoords(event);
    this.drawing = true;
    this.activePointerId = event.pointerId;
    this.startX = x;
    this.startY = y;
    this.canvasRef.nativeElement.setPointerCapture(event.pointerId);
  }

  onPointerMove(event: PointerEvent): void {
    if (!this.drawing) return;
    const { x, y } = this.toCanvasCoords(event);
    this.current = this.normalizeBox(this.startX, this.startY, x, y);
    this.hasSelection.set(true);
    this.redraw();
  }

  onPointerUp(event: PointerEvent): void {
    if (!this.drawing) return;
    this.drawing = false;
    this.releasePointer(event.pointerId);
    const { x, y } = this.toCanvasCoords(event);
    this.current = this.normalizeBox(this.startX, this.startY, x, y);
    if (this.current.width < 8 || this.current.height < 8) {
      this.current = null;
      this.hasSelection.set(false);
      this.selectionChange.emit(null);
    } else {
      this.hasSelection.set(true);
      this.selectionChange.emit(this.current);
    }
    this.redraw();
  }

  private releasePointer(pointerId?: number): void {
    const canvas = this.canvasRef?.nativeElement;
    const id = pointerId ?? this.activePointerId;
    this.drawing = false;
    this.activePointerId = null;
    if (!canvas || id === null) return;
    try {
      if (canvas.hasPointerCapture(id)) {
        canvas.releasePointerCapture(id);
      }
    } catch {
      // ignore — capture may already be released
    }
  }

  private loadImage(url: string): void {
    this.releasePointer();
    this.hasImage.set(false);
    this.hasSelection.set(false);
    this.current = null;
    this.selectionChange.emit(null);
    this.image = new Image();
    this.image.onload = () => {
      const canvas = this.canvasRef.nativeElement;
      const maxWidth = Math.min(960, canvas.parentElement?.clientWidth || 960);
      this.displayScale = Math.min(1, maxWidth / this.image.width);
      canvas.width = Math.round(this.image.width * this.displayScale);
      canvas.height = Math.round(this.image.height * this.displayScale);
      this.hasImage.set(true);
      this.redraw();
    };
    this.image.src = url;
  }

  private redraw(): void {
    const canvas = this.canvasRef.nativeElement;
    const ctx = canvas.getContext('2d');
    if (!ctx || !this.hasImage()) return;

    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.drawImage(this.image, 0, 0, canvas.width, canvas.height);

    if (!this.current) return;

    const sx = this.current.x * this.displayScale;
    const sy = this.current.y * this.displayScale;
    const sw = this.current.width * this.displayScale;
    const sh = this.current.height * this.displayScale;

    ctx.fillStyle = 'rgba(12, 16, 20, 0.5)';
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    ctx.clearRect(sx, sy, sw, sh);
    ctx.drawImage(
      this.image,
      this.current.x,
      this.current.y,
      this.current.width,
      this.current.height,
      sx,
      sy,
      sw,
      sh
    );

    ctx.strokeStyle = '#E8A45A';
    ctx.lineWidth = 2;
    ctx.setLineDash([6, 4]);
    ctx.strokeRect(sx + 1, sy + 1, Math.max(0, sw - 2), Math.max(0, sh - 2));
    ctx.setLineDash([]);
  }

  private toCanvasCoords(event: PointerEvent): { x: number; y: number } {
    const rect = this.canvasRef.nativeElement.getBoundingClientRect();
    const x = ((event.clientX - rect.left) / rect.width) * this.image.width;
    const y = ((event.clientY - rect.top) / rect.height) * this.image.height;
    return {
      x: Math.max(0, Math.min(this.image.width, x)),
      y: Math.max(0, Math.min(this.image.height, y))
    };
  }

  private normalizeBox(x0: number, y0: number, x1: number, y1: number): SelectionBox {
    const left = Math.min(x0, x1);
    const top = Math.min(y0, y1);
    return {
      x: left,
      y: top,
      width: Math.abs(x1 - x0),
      height: Math.abs(y1 - y0),
      imageWidth: this.image.width,
      imageHeight: this.image.height
    };
  }
}
