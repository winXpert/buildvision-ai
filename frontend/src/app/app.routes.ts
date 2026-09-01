import { Routes } from '@angular/router';
import { DesignerPageComponent } from './pages/designer/designer-page';

export const routes: Routes = [
  { path: '', component: DesignerPageComponent },
  { path: '**', redirectTo: '' }
];
