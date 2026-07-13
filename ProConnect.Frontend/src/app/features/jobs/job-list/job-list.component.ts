import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Subject, debounceTime, distinctUntilChanged, switchMap, startWith, takeUntil } from 'rxjs';
import { JobService, Job, JobFilter } from '../../../core/services/job.service';
import { AuthService } from '../../../core/services/auth.service';
import { environment } from '../../../../environments/environment';

interface ServiceCategory {
  id: number;
  name: string;
}

@Component({
  selector: 'app-job-list',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    ReactiveFormsModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatChipsModule,
    MatProgressSpinnerModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatCheckboxModule,
    MatPaginatorModule,
    MatTooltipModule
  ],
  templateUrl: './job-list.component.html',
  styleUrls: ['./job-list.component.css']
})
export class JobListComponent implements OnInit, OnDestroy {
  jobs: Job[] = [];
  categories: ServiceCategory[] = [];
  isLoading = true;
  errorMessage: string | null = null;

  filterForm: FormGroup;
  totalCount = 0;
  pageSize = 12;
  pageIndex = 0;

  readonly sortOptions = [
    { value: 'newest', label: 'Newest first' },
    { value: 'oldest', label: 'Oldest first' },
    { value: 'budgetHigh', label: 'Highest budget' },
    { value: 'budgetLow', label: 'Lowest budget' },
    { value: 'mostBids', label: 'Most bids' }
  ];

  readonly statusOptions = ['Open', 'Assigned', 'InProgress', 'Completed', 'Cancelled'];

  private readonly reload$ = new Subject<void>();
  private readonly destroy$ = new Subject<void>();

  constructor(
    private jobService: JobService,
    public authService: AuthService,
    private fb: FormBuilder,
    private http: HttpClient
  ) {
    this.filterForm = this.fb.group({
      search: [''],
      semantic: [false],
      categoryId: [null],
      status: ['Open'],
      isUrgent: [false],
      minBudget: [null],
      maxBudget: [null],
      location: [''],
      sortBy: ['newest']
    });
  }

  ngOnInit(): void {
    this.loadCategories();

    // Editing the filter bar re-queries the server, debounced so we don't fire per keystroke.
    this.filterForm.valueChanges
      .pipe(
        takeUntil(this.destroy$),
        debounceTime(350),
        distinctUntilChanged((a, b) => JSON.stringify(a) === JSON.stringify(b))
      )
      .subscribe(() => {
        this.pageIndex = 0; // a new filter always starts back at page 1
        this.reload$.next();
      });

    this.reload$
      .pipe(
        takeUntil(this.destroy$),
        startWith(undefined),
        switchMap(() => {
          this.isLoading = true;
          this.errorMessage = null;
          return this.jobService.getJobs(this.buildFilter());
        })
      )
      .subscribe({
        next: (result) => {
          this.jobs = result.items;
          this.totalCount = result.totalCount;
          this.isLoading = false;
        },
        error: (err) => {
          this.errorMessage = 'Failed to load jobs. Please try again.';
          this.isLoading = false;
          console.error(err);
        }
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  onPageChange(event: PageEvent): void {
    this.pageIndex = event.pageIndex;
    this.pageSize = event.pageSize;
    this.reload$.next();
  }

  resetFilters(): void {
    this.filterForm.reset({
      search: '',
      semantic: false,
      categoryId: null,
      status: 'Open',
      isUrgent: false,
      minBudget: null,
      maxBudget: null,
      location: '',
      sortBy: 'newest'
    });
  }

  imageFor(job: Job): string | null {
    return this.jobService.imageUrl(job);
  }

  getStatusColor(status: string): string {
    switch (status) {
      case 'Open': return 'primary';
      case 'Bidding': return 'accent';
      case 'Assigned': return 'warn';
      case 'InProgress': return 'accent';
      case 'Completed': return 'primary';
      case 'Cancelled': return 'warn';
      default: return '';
    }
  }

  private buildFilter(): JobFilter {
    const value = this.filterForm.value;
    return {
      search: value.search || undefined,
      // Only meaningful alongside a search term.
      semantic: value.search && value.semantic ? true : undefined,
      categoryId: value.categoryId ?? undefined,
      status: value.status || 'All',
      // Unchecked means "don't care", not "not urgent" — so send nothing.
      isUrgent: value.isUrgent ? true : undefined,
      minBudget: value.minBudget ?? undefined,
      maxBudget: value.maxBudget ?? undefined,
      location: value.location || undefined,
      sortBy: value.sortBy || 'newest',
      page: this.pageIndex + 1,
      pageSize: this.pageSize
    };
  }

  private loadCategories(): void {
    this.http.get<ServiceCategory[]>(`${environment.apiUrl}/ServiceCategories`).subscribe({
      next: (categories) => (this.categories = categories),
      error: (err) => console.error('Failed to load categories', err)
    });
  }
}
