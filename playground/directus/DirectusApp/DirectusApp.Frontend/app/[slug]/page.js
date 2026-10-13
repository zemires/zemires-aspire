import client from '../../lib/directus';
import { readItems } from '@directus/sdk';
import HeroSection from '../components/HeroSection';
import RichTextSection from '../components/RichTextSection';
import GallerySection from '../components/GallerySection';
import PricingSection from '../components/PricingSection';
import FormSection from '../components/FormSection';
import Header from '../components/Header';
import Footer from '../components/Footer';
import Posts from '../components/Posts';

export async function generateMetadata({params}) {
  const { slug } = await params

  // Fetch homepage data for SEO
  const seoData = await client.request(
    readItems('pages', {
      filter: {
        permalink: {
          _eq: `/${slug}`
        }
      },
      fields: [
        "seo"
      ]
    })
  );

  if (!seoData || seoData.length === 0) {
    return {
      title: 'Default Title',
      description: 'Default Description',
    };
  }

  const pageData = seoData[0];

  return {
    title: pageData.seo.title,
    description: pageData.seo.description,
  };
}

export default async function Page({params}) {

  const { slug } = await params;

  const homepageData = await client.request(
    readItems('pages', {
      filter: {
        permalink: {
          _eq: `/${slug}`
        }
      },
      fields: [
        "*",
        "blocks.*",
        "blocks.item.*.*.*",
      ]
    })
  );

  const hero_data = homepageData[0].blocks?.filter(block => block.collection === 'block_hero')?.[0];
  const rich_text_data = homepageData[0].blocks?.filter(block => block.collection === 'block_richtext')?.[0];
  const gallery_data = homepageData[0].blocks?.filter(block => block.collection === 'block_gallery')?.[0];
  const pricing_data = homepageData[0].blocks?.filter(block => block.collection === 'block_pricing')?.[0];
  const form_data = homepageData[0].blocks?.filter(block => block.collection === 'block_form')?.[0];
  const posts = homepageData[0].blocks?.filter(block => block.collection === 'block_posts')?.[0];

  const navigationData = await client.request(
    readItems('navigation', {
      fields: [
        "*.*.*.*",
      ]
    })
  );

  return (
    <main>
      <Header
        navigation={navigationData}
      />

      <>
        {hero_data && <HeroSection
          tagline={hero_data.item.tagline}
          headline={hero_data.item.headline}
          description={hero_data.item.description}
          image={hero_data.item.image}
          layout={hero_data.item.layout}
          button_group={hero_data.item.button_group.buttons}
        />}

        {rich_text_data && <RichTextSection
          {...(rich_text_data.item)}
        />}

        {gallery_data && <GallerySection
          {...(gallery_data.item)}
        />}

        {pricing_data && <PricingSection
          {...(pricing_data.item)} />}

        {posts && <Posts
          {...(posts.item)}
        />}

        {form_data && <FormSection
          {...(form_data.item)}
        />}

      </>

      <Footer
        navigation={navigationData}
      />

    </main>
  );
}