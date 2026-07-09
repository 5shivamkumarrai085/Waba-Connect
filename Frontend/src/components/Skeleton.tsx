import React from 'react';
import '../styles/Skeleton.css';

interface SkeletonProps {
  className?: string;
  variant?: 'text' | 'rect' | 'circle' | 'title' | 'card' | 'stat-card' | 'chart' | 'table' | 'list';
  width?: string | number;
  height?: string | number;
  count?: number;
  style?: React.CSSProperties;
}

export const Skeleton: React.FC<SkeletonProps> = ({
  className = '',
  variant = 'text',
  width,
  height,
  count = 1,
  style = {},
}) => {
  const customStyle: React.CSSProperties = {
    width: width,
    height: height,
    ...style,
  };

  const renderSingle = (index: number) => {
    if (variant === 'stat-card') {
      return (
        <div key={index} className="skeleton-stat-card skeleton-pulse" style={style}>
          <div className="skeleton-stat-top">
            <div className="skeleton-box variant-text" style={{ width: '60%' }} />
            <div className="skeleton-box variant-circle" style={{ width: 44, height: 44 }} />
          </div>
          <div className="skeleton-box variant-title" style={{ width: '40%', height: 32, marginTop: 12 }} />
          <div className="skeleton-box variant-text" style={{ width: '50%', marginTop: 12 }} />
        </div>
      );
    }

    if (variant === 'chart') {
      return (
        <div key={index} className="skeleton-chart skeleton-pulse" style={customStyle}>
          <div className="skeleton-chart-header">
            <div className="skeleton-box variant-title" style={{ width: '20%', height: 24 }} />
            <div className="skeleton-box variant-rect" style={{ width: 120, height: 32 }} />
          </div>
          <div className="skeleton-chart-body" />
        </div>
      );
    }

    if (variant === 'table') {
      return (
        <div key={index} className="skeleton-table skeleton-pulse" style={customStyle}>
          <div className="skeleton-table-header">
            {Array.from({ length: 5 }).map((_, i) => (
              <div key={i} className="skeleton-box variant-text" style={{ flex: 1, height: 20 }} />
            ))}
          </div>
          <div className="skeleton-table-body">
            {Array.from({ length: 5 }).map((_, rowIndex) => (
              <div key={rowIndex} className="skeleton-table-row">
                {Array.from({ length: 5 }).map((_, colIndex) => (
                  <div key={colIndex} className="skeleton-box variant-text" style={{ flex: 1, height: 16 }} />
                ))}
              </div>
            ))}
          </div>
        </div>
      );
    }

    if (variant === 'list') {
      return (
        <div key={index} className="skeleton-list skeleton-pulse" style={customStyle}>
          {Array.from({ length: 4 }).map((_, i) => (
            <div key={i} className="skeleton-list-item">
              <div className="skeleton-box variant-circle" style={{ width: 24, height: 24 }} />
              <div className="skeleton-box variant-text" style={{ flex: 1 }} />
            </div>
          ))}
        </div>
      );
    }

    const classes = `skeleton-box variant-${variant} skeleton-pulse ${className}`;
    return <div key={index} className={classes} style={customStyle} />;
  };

  if (count > 1 && variant !== 'table' && variant !== 'list') {
    return (
      <>
        {Array.from({ length: count }).map((_, i) => renderSingle(i))}
      </>
    );
  }

  return renderSingle(0);
};

export default Skeleton;
